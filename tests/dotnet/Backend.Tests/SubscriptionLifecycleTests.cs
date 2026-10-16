using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, étape 3 : cycle d'abonnement (un seul abonnement en vigueur,
    /// montée au prorata, descente à l'échéance, résiliation en fin de période,
    /// renouvellement par le solde, grâce de 3 jours puis coupure), essai par
    /// promotion une seule fois par compte et par public, durée des remises par
    /// code, recharge de tokens et codes promo sur les formations.
    /// </summary>
    public class SubscriptionLifecycleTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"cycle-{Guid.NewGuid()}";
        private const int Student = 9951, Parent = 9952, Admin = 9953, Other = 9954;

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);

        private static (SubscriptionLifecycleService Life, WalletService Wallet, EntitlementService Ent) Services(TestApplicationDbContext db)
        {
            var ent = new EntitlementService(db, new MemoryCache(new MemoryCacheOptions()));
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            return (new SubscriptionLifecycleService(db, ent, new PromotionService(db), wallet), wallet, ent);
        }

        private Subscriptions97bTestGridFamilies Seed(TestApplicationDbContext db)
        {
            db.Users.AddRange(
                new User { Id = Student, Email = "s@c.local", Role = "student", CreatedAt = DateTime.UtcNow.AddDays(-2) },
                new User { Id = Parent, Email = "p@c.local", Role = "parent", CreatedAt = DateTime.UtcNow.AddDays(-300) },
                new User { Id = Admin, Email = "a@c.local", Role = "admin" },
                new User { Id = Other, Email = "o@c.local", Role = "student", CreatedAt = DateTime.UtcNow.AddDays(-400) });
            db.SaveChanges();
            return new Subscriptions97bTestGridFamilies(Subscriptions97bTestGrid.Seed(db));
        }

        private static async Task Credit(WalletService wallet, int userId, decimal amount) =>
            await wallet.PostAsync(new WalletEntry(userId, WalletEntryTypes.Recharge, amount, $"test_recharge:{userId}:{Guid.NewGuid():N}", "Recharge de test"));

        private static PromoCode Promo(TestApplicationDbContext db, string code, string kind, decimal value = 0, string duration = PromotionDurations.FirstPayment,
            int? periods = null, int? trialDays = null, bool requiresCode = true, string appliesTo = "subscriptions")
        {
            var p = new PromoCode
            {
                Code = code, Kind = kind, DiscountType = kind == PromotionKinds.DiscountAmount ? "FixedAmount" : "Percentage",
                DiscountValue = value, SubscriptionDuration = duration, DurationPeriods = periods, TrialDays = trialDays,
                RequiresCode = requiresCode, AppliesTo = appliesTo, ValidFrom = DateTime.UtcNow.AddDays(-1), IsActive = true,
                CreatedBy = Admin, PerUserLimit = 5,
            };
            db.PromoCodes.Add(p);
            db.SaveChanges();
            return p;
        }

        [Fact]
        public async Task BalancePurchase_ChargesVersionPrice_AndKeepsOneSubscription()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, _) = Services(db);
            await Credit(wallet, Student, 100_000);

            var r1 = await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-standard"].PlanId, BillingPeriod = "yearly", Method = "balance" });
            Assert.Equal("activated", r1.Status);
            Assert.Equal(24_000, r1.AmountTtc); // 2 500 × 12 × 0,8 : prix TTC de la version, pas plan.Price × période
            Assert.Equal(76_000, await wallet.GetAvailableAsync(Student));

            var r2 = await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-premium"].PlanId, BillingPeriod = "yearly", Method = "balance" });
            Assert.Equal(SubscriptionChanges.Upgrade, r2.Quote.Change);
            var now = DateTime.UtcNow;
            var inForce = db.Subscriptions.Where(s => s.UserId == Student).AsEnumerable().Where(s => SubscriptionRules.IsInForce(s, now)).ToList();
            Assert.Single(inForce);
            Assert.Equal(g["student-premium"].PlanId, inForce[0].PricingPlanId);
        }

        [Fact]
        public async Task Upgrade_IsImmediate_WithProrataCredit()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, _) = Services(db);
            // Standard mensuel payé 2 500, à mi-période.
            var start = DateTime.UtcNow.AddDays(-15);
            db.Subscriptions.Add(new Subscription
            {
                UserId = Student, PricingPlanId = g["student-standard"].PlanId, PlanVersionId = g["student-standard"].VersionId,
                Audience = "student", BillingPeriod = "monthly", Status = "active", IsActive = true,
                StartDate = start, CurrentPeriodStart = start, EndDate = start.AddDays(30), PricePaidTtc = 2500,
            });
            db.SaveChanges();

            var q = await life.QuoteAsync(Student, g["student-premium"].PlanId, "monthly", null);
            Assert.Equal(SubscriptionChanges.Upgrade, q.Change);
            Assert.InRange(q.ProrataCreditTtc, 1240, 1260);
            Assert.Equal(5000 - q.ProrataCreditTtc, q.AmountDueTtc);
            Assert.True(q.EndsAt > DateTime.UtcNow.AddDays(27));

            await Credit(wallet, Student, 10_000);
            await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-premium"].PlanId, BillingPeriod = "monthly", Method = "balance" });
            var ent = await new EntitlementService(db, new MemoryCache(new MemoryCacheOptions())).ComputeAsync(Student);
            Assert.True(ent.Has(EntitlementCatalog.AiExamCoach)); // droits du Premium tout de suite
        }

        [Fact]
        public async Task Downgrade_IsScheduledAtPeriodEnd_AndAppliedByRenewal()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, _) = Services(db);
            var start = DateTime.UtcNow.AddDays(-10);
            var sub = new Subscription
            {
                UserId = Student, PricingPlanId = g["student-premium"].PlanId, PlanVersionId = g["student-premium"].VersionId,
                Audience = "student", BillingPeriod = "monthly", Status = "active", IsActive = true, AutoRenew = true,
                StartDate = start, CurrentPeriodStart = start, EndDate = start.AddDays(30), PricePaidTtc = 5000,
            };
            db.Subscriptions.Add(sub);
            db.SaveChanges();

            var r = await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-standard"].PlanId, BillingPeriod = "monthly", Method = "balance", AutoRenew = true });
            Assert.Equal("scheduled", r.Status);
            Assert.Equal(0, r.AmountTtc);
            db.ChangeTracker.Clear();
            Assert.Equal(g["student-premium"].PlanId, db.Subscriptions.Single(s => s.Id == sub.Id).PricingPlanId); // inchangé jusqu'à l'échéance

            await Credit(wallet, Student, 3000);
            var report = await life.RenewDueAsync(start.AddDays(30).AddMinutes(1));
            Assert.Equal(1, report.Renewed);
            db.ChangeTracker.Clear();
            var renewed = db.Subscriptions.Single(s => s.Id == sub.Id);
            Assert.Equal(g["student-standard"].PlanId, renewed.PricingPlanId);
            Assert.Equal(2500, renewed.PricePaidTtc);
            Assert.Null(renewed.PendingPricingPlanId);
            Assert.Equal(500, await wallet.GetAvailableAsync(Student));
        }

        [Fact]
        public async Task Cancel_KeepsAccessUntilPeriodEnd_ThenEnds()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, _, ent) = Services(db);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student", end: DateTime.UtcNow.AddDays(10));
            sub.AutoRenew = true; db.SaveChanges();

            var c = await life.CancelAsync(Student);
            Assert.True(c.CancelAtPeriodEnd);
            Assert.False(c.AutoRenew);
            Assert.True((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess)); // accès conservé

            await life.RenewDueAsync(DateTime.UtcNow.AddDays(11));
            db.ChangeTracker.Clear();
            var ended = db.Subscriptions.Single(s => s.Id == sub.Id);
            Assert.Equal(SubscriptionStatuses.Cancelled, ended.Status);
            Assert.False(ended.IsActive);
        }

        [Fact]
        public async Task Renewal_WithoutBalance_GivesThreeDaysGrace_ThenCutsAccess()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, ent) = Services(db);
            var end = DateTime.UtcNow.AddMinutes(-5);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student", end: end);
            sub.AutoRenew = true; db.SaveChanges();

            var r1 = await life.RenewDueAsync(DateTime.UtcNow);
            Assert.Equal(1, r1.InGrace);
            db.ChangeTracker.Clear();
            var pastDue = db.Subscriptions.Single(s => s.Id == sub.Id);
            Assert.Equal(SubscriptionStatuses.PastDue, pastDue.Status);
            Assert.Equal(end.AddDays(3), pastDue.GraceUntil);
            ent.Invalidate();
            Assert.True((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess)); // pendant la grâce
            Assert.NotEmpty(db.Notifications.Where(n => n.UserId == Student));

            // Fin de la grâce sans recharge : coupure.
            var r2 = await life.RenewDueAsync(end.AddDays(3).AddMinutes(1));
            Assert.Equal(1, r2.Expired);
            db.ChangeTracker.Clear();
            Assert.False(db.Subscriptions.Single(s => s.Id == sub.Id).IsActive);
            Assert.False((await ent.ComputeAsync(Student)).Has(EntitlementCatalog.CatalogAccess));
        }

        [Fact]
        public async Task Renewal_DuringGrace_SucceedsOnceBalanceIsToppedUp()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, _) = Services(db);
            var end = DateTime.UtcNow.AddMinutes(-5);
            var sub = Subscriptions97bTestGrid.Subscribe(db, Student, g["student-standard"], "student", end: end);
            sub.AutoRenew = true; db.SaveChanges();
            await life.RenewDueAsync(DateTime.UtcNow);

            await Credit(wallet, Student, 2500);
            var r = await life.RenewDueAsync(DateTime.UtcNow.AddHours(1));
            Assert.Equal(1, r.Renewed);
            db.ChangeTracker.Clear();
            var s = db.Subscriptions.Single(x => x.Id == sub.Id);
            Assert.Equal(SubscriptionStatuses.Active, s.Status);
            Assert.Null(s.GraceUntil);
            Assert.Equal(end.AddMonths(1), s.EndDate); // période comptée depuis l'échéance, pas depuis le paiement
            Assert.Equal(0, await wallet.GetAvailableAsync(Student));
        }

        [Fact]
        public async Task Trial_ByAutomaticPromotion_OnlyOncePerAccountAndAudience()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, _, ent) = Services(db);
            var trial = Promo(db, "ESSAI7", PromotionKinds.TrialDays, trialDays: 7, requiresCode: false);
            trial.Eligibility = PromotionEligibility.NewAccounts; trial.NewAccountDays = 30; db.SaveChanges();

            var q = await life.QuoteAsync(Student, g["student-premium"].PlanId, "monthly", null);
            Assert.Equal(SubscriptionChanges.Trial, q.Change);
            Assert.Equal(0, q.AmountDueTtc);
            var r = await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-premium"].PlanId, BillingPeriod = "monthly", Method = "balance" });
            Assert.Equal("activated", r.Status);
            var e = await ent.ComputeAsync(Student);
            Assert.Equal(SubscriptionStatuses.Trialing, e.Plan!.Status);
            Assert.True(e.Has(EntitlementCatalog.AiExamCoach));
            Assert.Single(db.PromoCodeUsages.Where(u => u.PromoCodeId == trial.Id && u.UserId == Student));

            // L'essai se termine sans renouvellement : expiration, puis plus jamais d'essai pour ce public.
            await life.RenewDueAsync(DateTime.UtcNow.AddDays(8));
            var again = await life.QuoteAsync(Student, g["student-premium"].PlanId, "monthly", null);
            Assert.Equal(SubscriptionChanges.New, again.Change);
            Assert.Equal(5000, again.AmountDueTtc);

            // Un compte ancien n'est pas éligible à l'offre « nouveaux comptes ».
            Assert.Equal(SubscriptionChanges.New, (await life.QuoteAsync(Other, g["student-premium"].PlanId, "monthly", null)).Change);
        }

        [Fact]
        public async Task PromoDuration_FirstPaymentOnly_Versus_NPeriods()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, wallet, _) = Services(db);
            Promo(db, "PREMIER20", PromotionKinds.DiscountPercent, 20);
            Promo(db, "TROIS50", PromotionKinds.DiscountPercent, 50, PromotionDurations.NPeriods, periods: 3);
            await Credit(wallet, Student, 50_000);
            await Credit(wallet, Other, 50_000);

            var a = await life.PurchaseAsync(Student, new SubscriptionPurchaseRequest { PlanId = g["student-standard"].PlanId, Method = "balance", PromoCode = "premier20", AutoRenew = true });
            Assert.Equal(2000, a.AmountTtc);
            var b = await life.PurchaseAsync(Other, new SubscriptionPurchaseRequest { PlanId = g["student-standard"].PlanId, Method = "balance", PromoCode = "TROIS50", AutoRenew = true });
            Assert.Equal(1250, b.AmountTtc);

            var due1 = DateTime.UtcNow.AddMonths(1).AddMinutes(5);
            await life.RenewDueAsync(due1);
            db.ChangeTracker.Clear();
            Assert.Equal(2500, db.Subscriptions.Single(s => s.UserId == Student && s.IsActive).PricePaidTtc); // remise du 1er paiement seulement
            var other = db.Subscriptions.Single(s => s.UserId == Other && s.IsActive);
            Assert.Equal(1250, other.PricePaidTtc);
            Assert.Equal(1, other.RemainingDiscountedPeriods);

            await life.RenewDueAsync(other.EndDate!.Value.AddMinutes(5));
            db.ChangeTracker.Clear();
            other = db.Subscriptions.Single(s => s.UserId == Other && s.IsActive);
            Assert.Equal(1250, other.PricePaidTtc); // 3e période remisée
            Assert.Null(other.PromoCodeId);
            await life.RenewDueAsync(other.EndDate!.Value.AddMinutes(5));
            db.ChangeTracker.Clear();
            Assert.Equal(2500, db.Subscriptions.Single(s => s.UserId == Other && s.IsActive).PricePaidTtc); // 4e : plein tarif
        }

        [Fact]
        public async Task PlanOfAnotherAudience_CannotBeBought()
        {
            using var db = Db();
            var g = Seed(db);
            var (life, _, _) = Services(db);
            await Assert.ThrowsAsync<SubscriptionLifecycleException>(() => life.QuoteAsync(Student, g["parent-famille"].PlanId, "monthly", null));
            await Assert.ThrowsAsync<SubscriptionLifecycleException>(() => life.QuoteAsync(Parent, g["student-free"].PlanId, "monthly", null));
        }

        [Fact]
        public async Task TokenTopUp_DebitsWallet_AndFeedsReserveUsedWhenLimitsReached()
        {
            using var db = Db();
            Seed(db);
            var (_, wallet, ent) = Services(db);
            await Credit(wallet, Student, 1000);
            var topUp = new TokenTopUpService(db, wallet, ent, new PromotionService(db));
            var q = await topUp.QuoteAsync(Student, 1, null);
            Assert.Equal(500, q.AmountDueTtc); // 250 000 tokens à 2 000 XAF le million
            var r = await topUp.TopUpAsync(Student, 1, null, "req-1");
            Assert.True(r.Succeeded);
            Assert.True((await topUp.TopUpAsync(Student, 1, null, "req-1")).Succeeded); // rejoué : pas de second débit
            Assert.Equal(500, await wallet.GetAvailableAsync(Student));
            Assert.Equal(250_000, await topUp.RemainingAsync(Student));

            // Limite de session atteinte (référence gratuite 200 k → session ≈ 16 666) : la réserve prend le relais.
            db.AiTokenUsages.Add(new AiTokenUsage { UserId = Student, ClientMessageId = "m0", TokensCharged = 20_000, IsFinalized = true, Source = "stream", CreatedAt = DateTime.UtcNow.AddMinutes(-10) });
            db.SaveChanges();
            var quota = new AiQuotaService(db, NullLogger<AiQuotaService>.Instance, ent);
            var d = await quota.CheckAndReserveAsync(Student, "m1", "stream");
            Assert.True(d.Allowed);
            Assert.True(db.AiTokenUsages.Single(u => u.ClientMessageId == "m1").FromTopUp);
        }

        [Fact]
        public async Task CoursePromo_ReducesCoursesTotal_NotOtherLines()
        {
            using var db = Db();
            Seed(db);
            Promo(db, "FORMATION10", PromotionKinds.DiscountPercent, 10, appliesTo: "courses");
            var service = new PromoCodeService(db, NullLogger<PromoCodeService>.Instance);
            var res = await service.ValidatePromoCodeAsync(Student, new ValidatePromoCodeRequest
            {
                Code = "FORMATION10", CartTotal = 15_000, SubjectIds = new() { 1 }, CatalogTotal = 5_000,
                CourseIds = new() { 7 }, CoursesTotal = 10_000,
            });
            Assert.True(res.IsValid, res.ErrorMessage);
            Assert.Equal(1_000, res.DiscountAmount); // 10 % des seules formations
            var catalogOnly = await service.ValidatePromoCodeAsync(Student, new ValidatePromoCodeRequest { Code = "FORMATION10", CartTotal = 5_000, SubjectIds = new() { 1 } });
            Assert.False(catalogOnly.IsValid);
        }
    }

    /// <summary>Accès par clé à la grille de test.</summary>
    public sealed class Subscriptions97bTestGridFamilies
    {
        private readonly System.Collections.Generic.Dictionary<string, Subscriptions97bTestGrid.Family> _f;
        public Subscriptions97bTestGridFamilies(System.Collections.Generic.Dictionary<string, Subscriptions97bTestGrid.Family> f) => _f = f;
        public Subscriptions97bTestGrid.Family this[string key] => _f[key];
    }
}
