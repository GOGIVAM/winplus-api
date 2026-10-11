using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.2 (suivi 17.8, 17.9, 17.10) : règle de remboursement et des frais
    /// NotchPay, bornes de la politique, et écritures de registre produites
    /// par un remboursement (vrai WalletService et vrai TutorRefundService sur
    /// le fournisseur InMemory, voir TestDb).
    /// </summary>
    public class TutorRefundRulesTests
    {
        // ── Règle pure ───────────────────────────────────────────────────

        [Fact]
        public void PayerCancelsLate_MobileMoney_FeeDeductedFromPartialRefund()
        {
            // 10 000 XAF payés en MoMo, frais 3 % = 300, politique 50 %.
            var d = TutorRefundRules.Compute(TutorRefundCause.PayerCancelled, 10000, 300, refundPercent: 50);
            Assert.Equal(4700, d.RefundXaf);
            Assert.Equal(TutorFeeBearer.Payer, d.FeeBorneBy);
            Assert.Equal(300, d.FeeRetainedXaf);
            Assert.Equal(5000, d.TutorRetainedXaf);
            // Remboursement + part tuteur + frais récupérés = prix encaissé.
            Assert.Equal(10000, d.RefundXaf + d.TutorRetainedXaf + d.FeeRetainedXaf);
        }

        [Fact]
        public void PayerCancelsEarly_Wallet_NoFee()
        {
            var d = TutorRefundRules.Compute(TutorRefundCause.PayerCancelled, 10000, 0, refundPercent: 100);
            Assert.Equal(10000, d.RefundXaf);
            Assert.Equal(TutorFeeBearer.None, d.FeeBorneBy);
            Assert.Equal(0, d.TutorRetainedXaf);
        }

        [Fact]
        public void PayerCancelsTooLate_NothingRefunded_TutorKeepsAll()
        {
            var d = TutorRefundRules.Compute(TutorRefundCause.PayerCancelled, 10000, 300, refundPercent: 0);
            Assert.Equal(0, d.RefundXaf);
            Assert.Equal(0, d.FeeRetainedXaf);
            Assert.Equal(TutorFeeBearer.None, d.FeeBorneBy);
            Assert.Equal(10000, d.TutorRetainedXaf);
        }

        [Fact]
        public void PayerCancels_RefundableBelowFee_NeverNegative()
        {
            var d = TutorRefundRules.Compute(TutorRefundCause.PayerCancelled, 1000, 300, refundPercent: 20);
            Assert.Equal(0, d.RefundXaf);
            Assert.Equal(200, d.FeeRetainedXaf);
            Assert.Equal(800, d.TutorRetainedXaf);
        }

        [Theory]
        [InlineData(TutorRefundCause.TutorCancelled)]
        [InlineData(TutorRefundCause.TutorDeclined)]
        [InlineData(TutorRefundCause.TutorNoResponse)]
        public void TutorResponsible_FullRefund_FeeOnTutor(TutorRefundCause cause)
        {
            var d = TutorRefundRules.Compute(cause, 10000, 300, refundPercent: 0);
            Assert.Equal(10000, d.RefundXaf);
            Assert.Equal(TutorFeeBearer.Tutor, d.FeeBorneBy);
            Assert.Equal(300, d.FeeRetainedXaf);
            Assert.Equal(0, d.TutorRetainedXaf);
        }

        [Fact]
        public void DisputeWonByPayer_FullRefund_FeeAbsorbedByPlatform()
        {
            var d = TutorRefundRules.Compute(TutorRefundCause.DisputeRefundFull, 10000, 300);
            Assert.Equal(10000, d.RefundXaf);
            Assert.Equal(TutorFeeBearer.Platform, d.FeeBorneBy);
            Assert.Equal(0, d.FeeRetainedXaf);
        }

        [Fact]
        public void DisputePartial_AmountClampedToPrice()
        {
            var d = TutorRefundRules.Compute(TutorRefundCause.DisputeRefundPartial, 10000, 300, disputeAmountXaf: 4000);
            Assert.Equal(4000, d.RefundXaf);
            Assert.Equal(6000, d.TutorRetainedXaf);
            Assert.Equal(40, d.RefundPercent);
            var over = TutorRefundRules.Compute(TutorRefundCause.DisputeRefundPartial, 10000, 300, disputeAmountXaf: 99999);
            Assert.Equal(10000, over.RefundXaf);
            Assert.Equal(0, over.TutorRetainedXaf);
        }

        [Theory]
        [InlineData(10000, 3, 300)]
        [InlineData(4550, 3, 137)] // 136,5 arrondi au XAF supérieur
        [InlineData(10000, 0, 0)]
        [InlineData(0, 3, 0)]
        public void NotchPayFee_RoundedToXaf(decimal price, decimal percent, decimal expected) =>
            Assert.Equal(expected, TutorRefundRules.NotchPayFee(price, percent));

        [Theory]
        [InlineData(30, 100)]
        [InlineData(24, 100)]
        [InlineData(10, 50)]
        [InlineData(2, 50)]
        [InlineData(1.5, 0)]
        public void RefundPercent_FollowsTutorThresholds(double hours, int expected) =>
            Assert.Equal(expected, TutorRefundRules.RefundPercentFor(hours, new TutorPolicy.CancellationSettings(24, 50, 2)));

        // ── Politique (17.10) et réglages (17.8) ─────────────────────────

        [Theory]
        [InlineData(11, 50, 2)]
        [InlineData(49, 50, 2)]
        [InlineData(24, 101, 2)]
        [InlineData(24, -1, 2)]
        [InlineData(24, 50, 13)]
        [InlineData(24, 50, -1)]
        public void Validate_RejectsOutOfBounds(int full, int partial, int none) =>
            Assert.Throws<InvalidOperationException>(() => TutorPolicy.Validate(full, partial, none));

        [Theory]
        [InlineData(12, 0, 0)]
        [InlineData(48, 100, 12)]
        [InlineData(24, 50, 2)]
        public void Validate_AcceptsBounds(int full, int partial, int none) =>
            TutorPolicy.Validate(full, partial, none);

        [Fact]
        public void Effective_ClampsLegacyValues()
        {
            var e = TutorPolicy.Effective(new TutorProfile { FullRefundHours = 168, PartialRefundPercent = 150, NoRefundHours = 30 });
            Assert.Equal(48, e.FullRefundHours);
            Assert.Equal(100, e.PartialRefundPercent);
            Assert.Equal(12, e.NoRefundHours);
        }

        [Fact]
        public void Settings_Defaults_24h_And_3Percent_ConfigurableAndBounded()
        {
            var defaults = TutorPolicySettings.From(new ConfigurationBuilder().Build());
            Assert.Equal(24, defaults.DisputeWindowHours);
            Assert.Equal(3m, defaults.NotchPayFeePercent);

            var custom = TutorPolicySettings.From(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TutorBooking:DisputeWindowHours"] = "1000",
                ["TutorBooking:NotchPayFeePercent"] = "2.5",
            }).Build());
            Assert.Equal(168, custom.DisputeWindowHours);
            Assert.Equal(2.5m, custom.NotchPayFeePercent);
        }
    }

    public class TutorRefundLedgerTests
    {
        private static readonly InMemoryDatabaseRoot _root = new();
        private readonly string _dbName = $"tutor-refund-{Guid.NewGuid()}";

        // Identifiants propres à cette classe (verrou de repli statique par utilisateur).
        private const int Tutor = 9201;
        private const int Student = 9202;
        private const int TutorProfileId = 9203;
        private const decimal Price = 10000;

        public TutorRefundLedgerTests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = Tutor, Email = "tuteur-92@test.local", Role = "teacher" },
                new User { Id = Student, Email = "eleve-92@test.local", Role = "student" });
            db.TutorProfiles.Add(new TutorProfile { Id = TutorProfileId, UserId = Tutor, IsActive = true, FullRefundHours = 24, PartialRefundPercent = 50, NoRefundHours = 2 });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, _root);

        private static WalletService Wallet(ApplicationDbContext db) => new(db, NullLogger<WalletService>.Instance);

        private static TutorRefundService Refunds(ApplicationDbContext db, Mock<INtfyService>? ntfy = null) =>
            new(db, Wallet(db), (ntfy ?? new Mock<INtfyService>()).Object, NullLogger<TutorRefundService>.Instance, new ConfigurationBuilder().Build());

        /// <summary>Réservation payée (revenu tuteur en attente posé) commençant dans <paramref name="hoursAhead"/> heures.</summary>
        private async Task<int> PaidBookingAsync(double hoursAhead, string method)
        {
            using var db = Fresh();
            var start = DateTime.UtcNow.AddHours(hoursAhead);
            var booking = new TutorBooking
            {
                TutorProfileId = TutorProfileId,
                StudentUserId = Student,
                PayerUserId = Student,
                SessionDate = DateOnly.FromDateTime(start),
                StartTime = new TimeSpan(start.Hour, start.Minute, 0),
                EndTime = new TimeSpan(start.Hour, start.Minute, 0) + TimeSpan.FromMinutes(30),
                PriceXaf = Price,
                Status = "pending_tutor_approval",
                PaymentStatus = "completed",
                PaymentMethod = method,
                PaymentFeeXaf = method == TutorPaymentMethods.Wallet ? 0 : 300,
            };
            db.TutorBookings.Add(booking);
            await db.SaveChangesAsync();
            await Wallet(db).SyncTutorBookingAsync(booking.Id);
            return booking.Id;
        }

        private async Task<List<WalletTransaction>> EntriesAsync(int bookingId)
        {
            using var db = Fresh();
            return await db.WalletTransactions.AsNoTracking()
                .Where(t => t.SourceType == "TutorBooking" && t.SourceId == bookingId).ToListAsync();
        }

        private static decimal ConfirmedSum(IEnumerable<WalletTransaction> rows, Func<WalletTransaction, bool>? filter = null) =>
            rows.Where(t => t.Status == WalletEntryStatus.Confirmed && (filter == null || filter(t))).Sum(t => t.Amount);

        [Fact]
        public async Task PayerCancelsLate_MobileMoney_CreditsPayerMinusFee_TutorKeepsShare_Idempotent()
        {
            var id = await PaidBookingAsync(hoursAhead: 10, TutorPaymentMethods.MobileMoney);

            using (var db = Fresh())
            {
                var booking = await db.TutorBookings.Include(b => b.TutorProfile).FirstAsync(b => b.Id == id);
                booking.Status = "cancelled";
                var decision = await Refunds(db).RefundAsync(booking, TutorRefundCause.PayerCancelled);
                Assert.NotNull(decision);
                Assert.Equal(50, decision!.RefundPercent);
                Assert.Equal(4700, decision.RefundXaf);
            }

            // Rejeu : double clic, réconciliation, tâche de fond qui repasse.
            using (var db = Fresh())
            {
                var booking = await db.TutorBookings.Include(b => b.TutorProfile).FirstAsync(b => b.Id == id);
                Assert.Null(await Refunds(db).RefundAsync(booking, TutorRefundCause.PayerCancelled));
                await Wallet(db).SyncTutorBookingAsync(id);
                await Wallet(db).SyncTutorBookingAsync(id);
            }

            var rows = await EntriesAsync(id);
            Assert.Single(rows, t => t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(4700, ConfirmedSum(rows, t => t.OwnerId == Student));
            // Part conservée 5 000 : 4 000 net tuteur (80 %), 1 000 commission.
            Assert.Equal(4000, ConfirmedSum(rows, t => t.OwnerId == Tutor));
            Assert.Equal(1000 + 300, ConfirmedSum(rows, t => t.OwnerType == WalletOwnerTypes.Platform));
            // Revenu en attente du prix plein : annulé, jamais confirmé.
            Assert.All(rows.Where(t => t.IdempotencyKey == WalletService.TutoringKey(id)), t => Assert.Equal(WalletEntryStatus.Reversed, t.Status));
            Assert.Equal(Price, ConfirmedSum(rows));
        }

        [Fact]
        public async Task TutorDeclines_FullRefund_FeeChargedToTutorWithinBalance()
        {
            var id = await PaidBookingAsync(hoursAhead: 72, TutorPaymentMethods.MobileMoney);
            using (var db = Fresh())
                await Wallet(db).PostAsync(new WalletEntry(Tutor, WalletEntryTypes.AdminCredit, 100, $"seed-{id}", "Crédit de test", "AdminCredit"));

            var ntfy = new Mock<INtfyService>();
            using (var db = Fresh())
            {
                var booking = await db.TutorBookings.Include(b => b.TutorProfile).FirstAsync(b => b.Id == id);
                booking.Status = "rejected";
                await Refunds(db, ntfy).RefundAsync(booking, TutorRefundCause.TutorDeclined);
            }

            var rows = await EntriesAsync(id);
            Assert.Equal(Price, ConfirmedSum(rows, t => t.OwnerId == Student));
            // Frais 300 mais seulement 100 disponibles : retenue bornée, jamais de solde négatif.
            Assert.Equal(-100, ConfirmedSum(rows, t => t.OwnerId == Tutor));
            Assert.Equal(100, ConfirmedSum(rows, t => t.EntryType == WalletEntryTypes.PaymentFeeRecovered));
            using (var db = Fresh())
                Assert.Equal(0, await Wallet(db).GetAvailableAsync(Tutor));
            // Plus d'alerte « remboursement manuel requis ».
            ntfy.Verify(n => n.PublishAdminAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Never);
        }

        [Fact]
        public async Task DisputeWonByPayer_WalletPayment_BalanceRestored_NoFeeEntries()
        {
            using (var db = Fresh())
                await Wallet(db).PostAsync(new WalletEntry(Student, WalletEntryTypes.Recharge, Price, $"recharge-{_dbName}", "Recharge", "WalletTopUp"));
            var id = await PaidBookingAsync(hoursAhead: 72, TutorPaymentMethods.Wallet);
            using (var db = Fresh())
                await Wallet(db).DebitAsync(new WalletEntry(Student, WalletEntryTypes.TutoringPayment, Price, WalletService.TutoringPaymentKey(id), "Séance", "TutorBooking", id));
            using (var db = Fresh())
                Assert.Equal(0, await Wallet(db).GetAvailableAsync(Student));

            using (var db = Fresh())
            {
                var booking = await db.TutorBookings.Include(b => b.TutorProfile).FirstAsync(b => b.Id == id);
                booking.Status = "cancelled";
                booking.DisputeResolution = "refunded_full";
                await Refunds(db).RefundAsync(booking, TutorRefundCause.DisputeRefundFull);
            }

            var rows = await EntriesAsync(id);
            using (var db = Fresh())
                Assert.Equal(Price, await Wallet(db).GetAvailableAsync(Student));
            Assert.DoesNotContain(rows, t => t.EntryType is WalletEntryTypes.TutoringFeeCharge or WalletEntryTypes.PaymentFeeRecovered);
            Assert.Equal(0, ConfirmedSum(rows, t => t.OwnerId == Tutor));
            // Débit du payeur compris, la séance remboursée pèse zéro au registre.
            Assert.Equal(0, ConfirmedSum(rows));
        }
    }
}
