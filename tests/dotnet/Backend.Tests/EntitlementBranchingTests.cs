using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, étape 2 : contrôles branchés sur les droits (children.max,
    /// part professeur figée au paiement, dotation calée sur la période,
    /// catalogue, audit de bascule).
    /// </summary>
    public class EntitlementBranchingTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"branch-{Guid.NewGuid()}";
        private const int Parent = 9901, ChildA = 9902, ChildB = 9903, ChildC = 9904, Teacher = 9905, Student = 9906;

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);
        private static EntitlementService Ent(TestApplicationDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()));

        public EntitlementBranchingTests()
        {
            using var db = Db();
            db.Users.AddRange(
                new User { Id = Parent, Email = "p@b.local", Role = "parent" },
                new User { Id = ChildA, Email = "a@b.local", Role = "student" },
                new User { Id = ChildB, Email = "b@b.local", Role = "student" },
                new User { Id = ChildC, Email = "c@b.local", Role = "student" },
                new User { Id = Teacher, Email = "t@b.local", Role = "teacher" },
                new User { Id = Student, Email = "s@b.local", Role = "student" });
            db.SaveChanges();
        }

        [Fact]
        public async Task ChildrenMax_FreeParentLinksOneChild_FamilleThree()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var ent = Ent(db);

            Assert.True((await ParentLinkLimits.CheckNewLinkAsync(db, ent, Parent, ChildA)).Allowed);
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = Parent, StudentId = ChildA, Status = "accepted", InitiatedBy = Parent });
            db.SaveChanges();
            var refused = await ParentLinkLimits.CheckNewLinkAsync(db, ent, Parent, ChildB);
            Assert.False(refused.Allowed);
            Assert.Equal(1, refused.Max);

            Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-famille"], "parent");
            ent.Invalidate();
            Assert.True((await ParentLinkLimits.CheckNewLinkAsync(db, ent, Parent, ChildB)).Allowed);
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = Parent, StudentId = ChildB, Status = "pending", InitiatedBy = Parent });
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = Parent, StudentId = ChildC, Status = "accepted", InitiatedBy = Parent });
            db.SaveChanges();
            // 3 liens (dont une demande en attente) : le 4e est refusé.
            Assert.False((await ParentLinkLimits.CheckNewLinkAsync(db, ent, Parent, 12345)).Allowed);
            // À l'acceptation, seules les liaisons acceptées comptent.
            Assert.True((await ParentLinkLimits.CheckNewLinkAsync(db, ent, Parent, ChildB, countPending: false)).Allowed);
        }

        [Fact]
        public async Task TeacherShare_IsLowestTierWithoutPlan_AndFrozenAtPayment()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            Assert.Equal(0.70m, await RevenueSplit.GetTeacherShareAsync(db, Teacher));

            var sub = Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-pro"], "teacher");
            Assert.Equal(0.75m, await RevenueSplit.GetTeacherShareAsync(db, Teacher));

            var start = DateTime.UtcNow.AddDays(2);
            var booking = new TutorBooking
            {
                TutorProfileId = 1, StudentUserId = Student, PayerUserId = Student,
                SessionDate = DateOnly.FromDateTime(start), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 0, 0),
                PriceXaf = 10000,
            };
            db.TutorBookings.Add(booking);
            db.SaveChanges();

            // Paiement : la part est figée à 75 %.
            Assert.Equal(0.75m, await RevenueSplit.FrozenBookingShareAsync(db, booking.Id, Teacher));

            // Le professeur passe Expert (80 %) : la séance déjà payée garde 75 %.
            sub.EndDate = DateTime.UtcNow.AddMinutes(-1);
            db.SaveChanges();
            Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-expert"], "teacher");
            Assert.Equal(0.80m, await RevenueSplit.GetTeacherShareAsync(db, Teacher));
            Assert.Equal(0.75m, await RevenueSplit.FrozenBookingShareAsync(db, booking.Id, Teacher));
            Assert.Equal(7500m, RevenueSplit.NetXaf(10000, 0.75m));
        }

        [Fact]
        public void ParentAllowance_CycleFollowsSubscriptionDate_NotCalendarMonth()
        {
            var start = new DateTime(2026, 9, 20, 8, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(3);
            var c1 = ParentAllowance.CurrentCycle(start, end, new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc))!;
            Assert.Equal(start, c1.Start);
            Assert.Equal(start.AddMonths(1), c1.End);
            var c2 = ParentAllowance.CurrentCycle(start, end, new DateTime(2026, 10, 21, 0, 0, 0, DateTimeKind.Utc))!;
            Assert.Equal(start.AddMonths(1), c2.Start);
            Assert.Null(ParentAllowance.CurrentCycle(start, end, end.AddDays(1)));
        }

        [Fact]
        public async Task TeacherOrParentPlan_NoLongerOpensCatalogToItself()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-famille-plus"], "parent");
            Assert.False((await Ent(db).ComputeAsync(Parent)).Has(EntitlementCatalog.CatalogAccess));
            Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-fondateur"], "teacher");
            Assert.True((await Ent(db).ComputeAsync(Teacher)).Has(EntitlementCatalog.CatalogAccess));
        }

        [Fact]
        public async Task MigrationAudit_MapsLegacyPlans_AndReportsDifferences()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var legacy = new PricingPlan { Name = "Premium", Category = "students", Price = 11900, Currency = "XAF" };
            var legacyTeacher = new PricingPlan { Name = "Pro", Category = "teachers", Price = 17900, Currency = "XAF" };
            db.PricingPlans.AddRange(legacy, legacyTeacher);
            db.SaveChanges();
            db.Subscriptions.Add(new Subscription { UserId = Student, PricingPlanId = legacy.Id, PlanName = "Premium", Status = "active", IsActive = true, StartDate = DateTime.UtcNow.AddDays(-3), EndDate = DateTime.UtcNow.AddDays(20) });
            // Plan professeur sur un compte élève : à résilier.
            db.Subscriptions.Add(new Subscription { UserId = ChildA, PricingPlanId = legacyTeacher.Id, PlanName = "Pro", Status = "active", IsActive = true, StartDate = DateTime.UtcNow.AddDays(-3), EndDate = DateTime.UtcNow.AddDays(20) });
            db.Subscriptions.Add(new Subscription { UserId = Teacher, PricingPlanId = legacyTeacher.Id, PlanName = "Pro", Status = "active", IsActive = true, StartDate = DateTime.UtcNow.AddDays(-3), EndDate = DateTime.UtcNow.AddDays(20) });
            db.SaveChanges();

            var rows = await new EntitlementMigrationAudit(db, Ent(db)).RunAsync();
            var s = rows.Single(r => r.UserId == Student);
            Assert.Equal("student-premium", s.TargetPlanKey);
            Assert.Empty(s.Differences); // catalogue ouvert et 2 M tokens des deux côtés
            var mismatch = rows.Single(r => r.UserId == ChildA);
            Assert.Null(mismatch.TargetPlanKey);
            Assert.Contains("autre public", mismatch.Action);
            var t = rows.Single(r => r.UserId == Teacher);
            Assert.Equal("teacher-pro", t.TargetPlanKey);
            Assert.Contains(t.Differences, d => d.Aspect == "part professeur" && d.After.Contains("75"));
        }
    }
}
