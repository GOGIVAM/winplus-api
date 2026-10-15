using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, étape 1 : résolution des droits (gratuit, plan, parent
    /// donateur, expiration, grâce), cache invalidé, publication de version
    /// (amélioration immédiate / préavis), octroi avec contrôle du public.
    /// </summary>
    public class EntitlementServiceTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"ent-{Guid.NewGuid()}";
        private const int Student = 9801, Parent = 9802, Teacher = 9803, Child2 = 9804, Child3 = 9805, Admin = 9806;

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);

        private static EntitlementService Service(TestApplicationDbContext db) =>
            new(db, new MemoryCache(new MemoryCacheOptions()));

        public EntitlementServiceTests()
        {
            using var db = Db();
            db.Users.AddRange(
                new User { Id = Student, Email = "s@t.local", Role = "student" },
                new User { Id = Parent, Email = "p@t.local", Role = "parent" },
                new User { Id = Teacher, Email = "t@t.local", Role = "teacher" },
                new User { Id = Child2, Email = "c2@t.local", Role = "student" },
                new User { Id = Child3, Email = "c3@t.local", Role = "student" },
                new User { Id = Admin, Email = "a@t.local", Role = "admin" });
            db.SaveChanges();
        }

        [Fact]
        public async Task FreeAccount_GetsFreeTierValues_FromDatabaseOrCatalog()
        {
            using var db = Db();
            // Sans grille en base : valeurs par défaut du catalogue.
            var e = await Service(db).ComputeAsync(Student);
            Assert.False(e.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(200_000, e.PersonalReferenceTokens);
            Assert.Equal(1, e.AiMultiplier);
            Assert.Null(e.Plan);

            Subscriptions97bTestGrid.Seed(db);
            var t = await Service(db).ComputeAsync(Teacher);
            Assert.Equal(0.70m, t.Fraction(EntitlementCatalog.TeacherRevenueShare));
            Assert.Equal(1, t.Int(EntitlementCatalog.TeacherMaxClasses));
        }

        [Fact]
        public async Task PaidPlan_ReplacesFreeValues_AndExpiresAtEndDate()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-premium"], "student");

            var e = await Service(db).ComputeAsync(Student);
            Assert.True(e.Has(EntitlementCatalog.CatalogAccess));
            Assert.True(e.Has(EntitlementCatalog.AiExamCoach));
            Assert.Equal(10, e.AiMultiplier);
            Assert.True(e.IsUnlimited(EntitlementCatalog.FavoritesMax));
            Assert.Equal("Premium", e.Plan!.PlanName);

            sub.EndDate = DateTime.UtcNow.AddMinutes(-1);
            db.SaveChanges();
            var expired = await Service(db).ComputeAsync(Student);
            Assert.False(expired.Has(EntitlementCatalog.CatalogAccess));
            Assert.Null(expired.Plan);
        }

        [Fact]
        public async Task PastDue_KeepsRightsDuringGrace_ThenCutsThem()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student",
                end: DateTime.UtcNow.AddDays(-1), status: SubscriptionStatuses.PastDue, grace: DateTime.UtcNow.AddDays(2));

            Assert.True((await Service(db).ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));

            sub.GraceUntil = DateTime.UtcNow.AddSeconds(-1);
            db.SaveChanges();
            Assert.False((await Service(db).ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
        }

        [Fact]
        public async Task PlanOfAnotherAudience_OpensNothing()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            // Ligne incohérente héritée : un plan professeur sur un compte élève.
            Subscriptions97bTestGrid.Subscribe(db, Student, g["teacher-expert"], "teacher");
            var e = await Service(db).ComputeAsync(Student);
            Assert.False(e.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(1, e.AiMultiplier);
        }

        [Fact]
        public async Task ParentPlan_OpensCatalogAndBonus_OnlyToCoveredChildren()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var t0 = DateTime.UtcNow.AddDays(-10);
            db.ParentStudentLinks.AddRange(
                new ParentStudentLink { ParentId = Parent, StudentId = Student, Status = "accepted", CreatedAt = t0 },
                new ParentStudentLink { ParentId = Parent, StudentId = Child2, Status = "accepted", CreatedAt = t0.AddDays(1) });
            db.SaveChanges();
            // Basique : un seul enfant couvert, le plus ancien lien.
            Subscriptions97bTestGrid.Subscribe(db, Parent, g["parent-basique"], "parent");

            var first = await Service(db).ComputeAsync(Student);
            Assert.True(first.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(400_000, first.ParentBonusTokens);
            Assert.Equal(3, first.AiMultiplier); // (200 k + 400 k) / 200 k

            var second = await Service(db).ComputeAsync(Child2);
            Assert.False(second.Has(EntitlementCatalog.CatalogAccess));
            Assert.Equal(0, second.ParentBonusTokens);
        }

        [Fact]
        public async Task ParentBonus_IsSharedBeyondShares()
        {
            Assert.Equal(400_000, EntitlementService.SharedBonus(400_000, 3, 3));
            Assert.Equal(300_000, EntitlementService.SharedBonus(400_000, 3, 4));
            Assert.Equal(0, EntitlementService.SharedBonus(0, 3, 1));
            Assert.Equal(500_000, EntitlementService.SharedBonus(500_000, -1, 50));
            await Task.CompletedTask;
        }

        [Fact]
        public async Task Cache_IsInvalidated_AfterChange()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var svc = Service(db);
            Assert.False((await svc.GetAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
            Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student");
            // Toujours en cache tant que rien n'invalide.
            Assert.False((await svc.GetAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
            svc.Invalidate();
            Assert.True((await svc.GetAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
        }

        [Fact]
        public async Task Publish_Improvement_MovesSubscribersNow_Downgrade_WaitsForNotice()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student");
            var svc = Service(db);
            var plans = new PlanCatalogService(db, svc);

            // Amélioration : prix identique, quota relevé.
            var draft = await plans.CreateDraftAsync(g["student-standard"].PlanId, Admin);
            await plans.UpdateDraftAsync(draft.Id, new UpdatePlanVersionRequest
            {
                Entitlements = new() { ["ai.reference_tokens"] = "1000000" },
            });
            var r1 = await plans.PublishAsync(draft.Id, Admin);
            Assert.True(r1.IsImprovement);
            Assert.Equal(1, r1.SubscribersMovedNow);
            db.ChangeTracker.Clear();
            Assert.Equal(draft.Id, db.Subscriptions.Single(s => s.Id == sub.Id).PlanVersionId);
            Assert.Equal(5, (await svc.GetAsync(Student)).AiMultiplier);

            // Hausse de prix : l'abonné garde sa version, prévenu, bascule au renouvellement après préavis.
            var d2 = await plans.CreateDraftAsync(g["student-standard"].PlanId, Admin);
            await plans.UpdateDraftAsync(d2.Id, new UpdatePlanVersionRequest { Prices = new() { ["monthly"] = 3000 } });
            var r2 = await plans.PublishAsync(d2.Id, Admin);
            Assert.False(r2.IsImprovement);
            Assert.Equal(0, r2.SubscribersMovedNow);
            Assert.Equal(1, r2.SubscribersNotified);
            db.ChangeTracker.Clear();
            var kept = db.Subscriptions.Single(s => s.Id == sub.Id);
            Assert.Equal(draft.Id, kept.PlanVersionId);
            Assert.True(r2.ExistingSubscribersFrom > DateTime.UtcNow.AddDays(29));
            Assert.Single(db.Notifications.Where(n => n.UserId == Student));

            var beforeNotice = await PlanCatalogService.VersionForRenewalAsync(db, kept, DateTime.UtcNow.AddDays(10));
            Assert.Equal(draft.Id, beforeNotice!.Id);
            var afterNotice = await PlanCatalogService.VersionForRenewalAsync(db, kept, DateTime.UtcNow.AddDays(31));
            Assert.Equal(d2.Id, afterNotice!.Id);
        }

        [Fact]
        public async Task Draft_RejectsInvalidValues_AndForeignAudienceKeys()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var plans = new PlanCatalogService(db, Service(db));
            var d = await plans.CreateDraftAsync(g["student-standard"].PlanId, Admin);
            await Assert.ThrowsAsync<PlanServiceException>(() => plans.UpdateDraftAsync(d.Id,
                new UpdatePlanVersionRequest { Entitlements = new() { ["catalog.access"] = "peut-être" } }));
            await Assert.ThrowsAsync<PlanServiceException>(() => plans.UpdateDraftAsync(d.Id,
                new UpdatePlanVersionRequest { Entitlements = new() { ["teacher.revenue_share"] = "90" } }));
            await Assert.ThrowsAsync<PlanServiceException>(() => plans.UpdateDraftAsync(d.Id,
                new UpdatePlanVersionRequest { Entitlements = new() { ["ai.reference_tokens"] = "illimité" } }));
            await Assert.ThrowsAsync<PlanServiceException>(() => plans.UpdateDraftAsync(d.Id,
                new UpdatePlanVersionRequest { Prices = new() { ["weekly"] = 100 } }));
        }

        [Fact]
        public async Task Grant_ChecksAudience_AndKeepsOneSubscription()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var svc = Service(db);
            var plans = new PlanCatalogService(db, svc);

            await Assert.ThrowsAsync<PlanServiceException>(() => plans.GrantAsync(new GrantPlanRequest
            { UserId = Teacher, PlanId = g["student-premium"].PlanId, Reason = "test" }));
            await Assert.ThrowsAsync<PlanServiceException>(() => plans.GrantAsync(new GrantPlanRequest
            { UserId = Student, PlanId = g["student-free"].PlanId, Reason = "test" }));

            await plans.GrantAsync(new GrantPlanRequest { UserId = Student, PlanId = g["student-standard"].PlanId, BillingPeriod = "monthly" });
            await db.SaveChangesAsync();
            await plans.GrantAsync(new GrantPlanRequest { UserId = Student, PlanId = g["student-premium"].PlanId, BillingPeriod = "yearly" });
            await db.SaveChangesAsync();

            var now = DateTime.UtcNow;
            var inForce = db.Subscriptions.Where(s => s.UserId == Student).AsEnumerable().Where(s => SubscriptionRules.IsInForce(s, now)).ToList();
            var only = Assert.Single(inForce);
            Assert.Equal(g["student-premium"].VersionId, only.PlanVersionId);
            Assert.True(only.EndDate > now.AddMonths(11));
        }

        [Fact]
        public void Catalog_NormalizesAndCompares()
        {
            Assert.Equal("true", EntitlementCatalog.Normalize("catalog.access", "Oui", out _));
            Assert.Equal("-1", EntitlementCatalog.Normalize("favorites.max", "illimité", out _));
            Assert.Equal("75", EntitlementCatalog.Normalize("teacher.revenue_share", "75 %", out _));
            Assert.Null(EntitlementCatalog.Normalize("teacher.revenue_share", "120", out var err));
            Assert.NotNull(err);
            Assert.Equal(1, EntitlementCatalog.Compare("favorites.max", "20", "-1"));
            Assert.Equal(-1, EntitlementCatalog.Compare("favorites.max", "-1", "200"));
            Assert.Equal("-1", EntitlementCatalog.Merge("groups.max_owned", "3", "-1"));
            Assert.Equal("true", EntitlementCatalog.Merge("catalog.access", "false", "true"));
        }
    }
}
