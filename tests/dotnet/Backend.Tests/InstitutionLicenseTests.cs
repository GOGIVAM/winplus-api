using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, étape 4 : licences d'établissement (tarif par volume, sièges,
    /// droits ouverts aux élèves et enseignants, part Pro 75 % du siège
    /// enseignant, essai une seule fois, renouvellement par le portefeuille,
    /// grâce puis expiration).
    /// </summary>
    public class InstitutionLicenseTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"lic-{Guid.NewGuid()}";
        private const int InstitutionId = 970, InstUser = 9971, Student = 9972, Student2 = 9973, Teacher = 9974, Admin = 9975, Outsider = 9976;

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);

        public InstitutionLicenseTests()
        {
            using var db = Db();
            db.Institutions.Add(new Institution { Id = InstitutionId, Name = "Lycée Test", WalletOwnerUserId = InstUser });
            db.Users.AddRange(
                new User { Id = InstUser, Email = "inst@l.local", Role = "institution", InstitutionId = InstitutionId },
                new User { Id = Student, Email = "s1@l.local", Role = "student" },
                new User { Id = Student2, Email = "s2@l.local", Role = "student" },
                new User { Id = Teacher, Email = "t@l.local", Role = "teacher" },
                new User { Id = Admin, Email = "a@l.local", Role = "admin" },
                new User { Id = Outsider, Email = "o@l.local", Role = "student" });
            db.InstitutionStudents.AddRange(
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = Student, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Accepted, RespondedAt = DateTime.UtcNow },
                new InstitutionStudent { InstitutionId = InstitutionId, StudentId = Student2, IsActive = true, ConfirmationStatus = InstitutionAttachmentStatuses.Pending });
            db.InstitutionTeacherLinks.Add(new InstitutionTeacherLink { InstitutionId = InstitutionId, TeacherId = Teacher, Status = "accepted", InitiatedBy = InstUser });
            db.SaveChanges();
        }

        private static (InstitutionLicenseService Svc, WalletService Wallet, EntitlementService Ent) Services(TestApplicationDbContext db)
        {
            var ent = new EntitlementService(db, new MemoryCache(new MemoryCacheOptions()));
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            return (new InstitutionLicenseService(db, wallet, new PromotionService(db), ent), wallet, ent);
        }

        private static Task Credit(WalletService w, decimal amount) =>
            w.PostAsync(new WalletEntry(InstUser, WalletEntryTypes.Recharge, amount, $"inst_recharge:{Guid.NewGuid():N}", "Recharge", OwnerType: WalletOwnerTypes.Institution));

        [Fact]
        public void Policy_PricesByVolume_AndTeacherSeats()
        {
            Assert.Equal(6000, LicensePolicy.YearlyPricePerSeat(100));
            Assert.Equal(4800, LicensePolicy.YearlyPricePerSeat(400));
            Assert.Equal(3600, LicensePolicy.YearlyPricePerSeat(1500));
            Assert.Equal(3, LicensePolicy.TeacherSeatsFor(20));
            Assert.Equal(7, LicensePolicy.TeacherSeatsFor(100));
            Assert.Equal(new DateTime(2027, 7, 31, 23, 59, 59, DateTimeKind.Utc), LicensePolicy.EndFor(LicenseTerms.SchoolYear, new DateTime(2026, 10, 16, 0, 0, 0, DateTimeKind.Utc)));
        }

        [Fact]
        public async Task Purchase_DebitsInstitutionWallet_AssignsSeats_AndOpensRights()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var (svc, wallet, ent) = Services(db);
            await Credit(wallet, 200_000);
            await Assert.ThrowsAsync<LicenseException>(() => svc.PurchaseAsync(InstUser, InstitutionId, 10, "year", null, false)); // < 20 sièges

            var l = await svc.PurchaseAsync(InstUser, InstitutionId, 20, "year", null, true);
            Assert.Equal(120_000, l.TotalPriceTtc);
            Assert.Equal(80_000, await wallet.GetAvailableAsync(InstUser));
            db.ChangeTracker.Clear();
            var seats = db.InstitutionSeats.Where(s => s.LicenseId == l.Id && s.ReleasedAt == null).ToList();
            Assert.Contains(seats, s => s.UserId == Student && s.Kind == SeatKinds.Student);
            Assert.Contains(seats, s => s.UserId == Teacher && s.Kind == SeatKinds.Teacher);
            Assert.DoesNotContain(seats, s => s.UserId == Student2); // rattachement non accepté

            var s1 = await ent.ComputeAsync(Student);
            Assert.True(s1.Has(EntitlementCatalog.CatalogAccess));
            Assert.True(s1.Has(EntitlementCatalog.AiExamCoach));
            Assert.Equal(LicensePolicy.StudentBonusTokens, s1.LicenseBonusTokens);

            // Siège enseignant : part du plan Pro (75 %) quand le plan personnel est inférieur.
            Assert.Equal(0.75m, await RevenueSplit.GetTeacherShareAsync(db, Teacher));
            var t = await ent.ComputeAsync(Teacher);
            Assert.Equal(10, t.Int(EntitlementCatalog.TeacherMaxClasses));
            Assert.True(t.PersonalReferenceTokens >= LicensePolicy.TeacherReferenceTokens);

            await Assert.ThrowsAsync<LicenseException>(() => svc.AssignSeatAsync(InstitutionId, SeatKinds.Student, Outsider, Admin)); // non rattaché
        }

        [Fact]
        public async Task TeacherSeat_DoesNotLowerAnExpertShare()
        {
            using var db = Db();
            var g = Subscriptions97bTestGrid.Seed(db);
            var (svc, wallet, _) = Services(db);
            await Credit(wallet, 200_000);
            Subscriptions97bTestGrid.Subscribe(db, Teacher, g["teacher-expert"], "teacher");
            await svc.PurchaseAsync(InstUser, InstitutionId, 20, "year", null, false);
            Assert.Equal(0.80m, await RevenueSplit.GetTeacherShareAsync(db, Teacher));
        }

        [Fact]
        public async Task Trial_ByPromotion_OncePerInstitution()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var (svc, _, ent) = Services(db);
            db.PromoCodes.Add(new PromoCode
            {
                Code = "ESSAIECOLE", Kind = PromotionKinds.TrialDays, TrialDays = 30, RequiresCode = false, AppliesTo = "institution_license",
                DiscountType = "Percentage", ValidFrom = DateTime.UtcNow.AddDays(-1), IsActive = true, CreatedBy = Admin, PerUserLimit = 5,
            });
            db.SaveChanges();
            var l = await svc.StartTrialAsync(InstUser, InstitutionId, null);
            Assert.Equal(LicenseStatuses.Trialing, l.Status);
            Assert.Equal(30, l.StudentSeats);
            Assert.True((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
            await Assert.ThrowsAsync<LicenseException>(() => svc.StartTrialAsync(InstUser, InstitutionId, null));

            // Fin de l'essai : licence terminée, droits retirés.
            await svc.RenewDueAsync(DateTime.UtcNow.AddDays(31));
            ent.Invalidate();
            Assert.False((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
        }

        [Fact]
        public async Task Renewal_FromWallet_ThenGrace_ThenExpiry()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var (svc, wallet, _) = Services(db);
            await Credit(wallet, 120_000);
            var l = await svc.PurchaseAsync(InstUser, InstitutionId, 20, "quarter", null, true);
            Assert.Equal(20 * Math.Round(6000m * 3 / 11), l.TotalPriceTtc);

            var end = l.EndDate;
            await Credit(wallet, l.TotalPriceTtc);
            var r1 = await svc.RenewDueAsync(end.AddMinutes(1));
            Assert.Equal(1, r1.Renewed);
            db.ChangeTracker.Clear();
            var renewed = db.InstitutionLicenses.Single(x => x.Id == l.Id);
            Assert.Equal(end.AddMonths(3), renewed.EndDate);

            // Solde vide à l'échéance suivante : grâce de 3 jours, puis expiration.
            var balance = await wallet.GetAvailableAsync(InstUser);
            if (balance > 0) await wallet.PostAsync(new WalletEntry(InstUser, WalletEntryTypes.AdminCredit, -balance, $"drain:{Guid.NewGuid():N}", "Vidage", OwnerType: WalletOwnerTypes.Institution));
            var r2 = await svc.RenewDueAsync(renewed.EndDate.AddMinutes(1));
            Assert.Equal(1, r2.InGrace);
            db.ChangeTracker.Clear();
            var pastDue = db.InstitutionLicenses.Single(x => x.Id == l.Id);
            Assert.Equal(LicenseStatuses.PastDue, pastDue.Status);
            var r3 = await svc.RenewDueAsync(pastDue.GraceUntil!.Value.AddMinutes(1));
            Assert.Equal(1, r3.Expired);
        }
    }
}
