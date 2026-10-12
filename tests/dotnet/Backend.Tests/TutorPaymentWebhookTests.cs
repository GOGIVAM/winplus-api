using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
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
    /// Webhook NotchPay des parcours du 9.3 (suivi 17.14, B2) : séance payée en
    /// Mobile Money pour autrui (parent pour son enfant, institution pour son
    /// élève) et recharge du portefeuille institution. Le traitement retrouve
    /// la bonne réservation ou la bonne recharge par la référence de paiement,
    /// crédite le bon portefeuille, fait passer la séance en attente du tuteur,
    /// et reste idempotent.
    /// </summary>
    public class TutorPaymentWebhookTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"webhook-{Guid.NewGuid()}";
        private readonly Mock<INtfyService> _ntfy = new();
        private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

        private const int Tutor = 9501, Parent = 9502, Child = 9503, InstHolder = 9504, InstAccount = 9505, Pupil = 9506;
        private const int TutorProfileId = 9550, InstitutionId = 9551;

        public TutorPaymentWebhookTests()
        {
            using var db = Fresh();
            User U(int id, string role, int? inst = null) => new() { Id = id, Email = $"w{id}@test.local", Role = role, IsActive = true, InstitutionId = inst };
            db.Users.AddRange(U(Tutor, "teacher"), U(Parent, "parent"), U(Child, "student"),
                U(InstHolder, "institution", InstitutionId), U(InstAccount, "institution", InstitutionId), U(Pupil, "student"));
            db.Institutions.Add(new Institution { Id = InstitutionId, Name = "Lycée Webhook", Country = "CM", WalletOwnerUserId = InstHolder });
            db.TutorProfiles.Add(new TutorProfile { Id = TutorProfileId, UserId = Tutor, IsActive = true });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);

        private TutorBookingService Bookings(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorBookingService(db, new Mock<INotchPayService>().Object, _ntfy.Object, NullLogger<TutorBookingService>.Instance, wallet, Config, refunds);
        }

        private async Task<int> SeedMobileMoneyBookingAsync(string reference, string payerType, int payer, int student, int? institutionId = null)
        {
            using var db = Fresh();
            var start = DateTime.UtcNow.AddDays(5);
            var booking = new TutorBooking
            {
                TutorProfileId = TutorProfileId, StudentUserId = student, PayerType = payerType, PayerUserId = payer,
                BookedByUserId = payerType == TutorPayerTypes.Institution ? InstAccount : payer, PayerInstitutionId = institutionId,
                SessionDate = DateOnly.FromDateTime(start), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 30, 0),
                PriceXaf = 9000, Status = "pending_payment", PaymentStatus = "pending", PaymentMethod = TutorPaymentMethods.MobileMoney,
                NotchpayReference = reference, PhoneNumber = "677000000",
                Participants = new List<TutorBookingParticipant> { new() { StudentUserId = student } },
            };
            db.TutorBookings.Add(booking);
            await db.SaveChangesAsync();
            return booking.Id;
        }

        private async Task<bool> WebhookAsync(string reference, string status, decimal? amount = 9000)
        {
            using var db = Fresh();
            return await Bookings(db).TryHandleNotchPayWebhookAsync(reference, "payment.complete",
                new NotchPayWebhookTransaction { Reference = reference, Status = status, Amount = amount, Currency = "XAF" });
        }

        [Fact]
        public async Task ParentPaysForChild_WebhookCompleted_PendingTutor_RevenuePending_EveryoneNotified_Idempotent()
        {
            var id = await SeedMobileMoneyBookingAsync("TBK-9001-1", TutorPayerTypes.Parent, Parent, Child);

            Assert.True(await WebhookAsync("TBK-9001-1", "complete"));
            Assert.True(await WebhookAsync("TBK-9001-1", "complete")); // rejeu NotchPay

            using var check = Fresh();
            var b = await check.TutorBookings.AsNoTracking().FirstAsync(x => x.Id == id);
            Assert.Equal("pending_tutor_approval", b.Status);
            Assert.Equal("completed", b.PaymentStatus);
            Assert.True(b.PaymentFeeXaf > 0); // frais NotchPay figés à l'encaissement
            var revenue = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(Tutor, revenue.OwnerId);
            Assert.Equal(WalletEntryStatus.Pending, revenue.Status);
            // Aucun débit de portefeuille : payé en Mobile Money.
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringPayment));

            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{Tutor}", "Nouvelle demande de réservation", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), Tutor, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{Parent}", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), Parent, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{Child}", "Une séance a été réservée pour toi", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), Child, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task WebhookFailed_CancelsBooking_NoLedgerEntry()
        {
            var id = await SeedMobileMoneyBookingAsync("TBK-9002-1", TutorPayerTypes.Parent, Parent, Child);
            Assert.True(await WebhookAsync("TBK-9002-1", "failed"));
            using var check = Fresh();
            var b = await check.TutorBookings.AsNoTracking().FirstAsync(x => x.Id == id);
            Assert.Equal("cancelled", b.Status);
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.SourceId == id && t.SourceType == "TutorBooking"));
        }

        [Fact]
        public async Task WebhookWithWrongAmount_DoesNotConfirm_AdminAlerted()
        {
            var id = await SeedMobileMoneyBookingAsync("TBK-9003-1", TutorPayerTypes.Parent, Parent, Child);
            Assert.True(await WebhookAsync("TBK-9003-1", "complete", amount: 100));
            using var check = Fresh();
            var b = await check.TutorBookings.AsNoTracking().FirstAsync(x => x.Id == id);
            Assert.Equal("pending_payment", b.Status);
            Assert.Equal("pending", b.PaymentStatus);
            _ntfy.Verify(n => n.PublishAdminAsync(It.Is<string>(t => t.Contains("Écart")), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task UnknownOrForeignReference_IsNotHandledAsBooking()
        {
            Assert.False(await WebhookAsync("WP-123-1", "complete"));
            Assert.True(await WebhookAsync("TBK-404-1", "complete")); // préfixe de séance, réservation absente : acquitté, rien écrit
        }

        [Fact]
        public async Task PaymentConfirmedAfterParentCancelled_RefundGoesToParentWallet_NotTheChild()
        {
            var id = await SeedMobileMoneyBookingAsync("TBK-9004-1", TutorPayerTypes.Parent, Parent, Child);
            using (var db = Fresh())
            {
                var b = await db.TutorBookings.FirstAsync(x => x.Id == id);
                b.Status = "cancelled";
                b.CancelledByUserId = Parent;
                await db.SaveChangesAsync();
            }
            Assert.True(await WebhookAsync("TBK-9004-1", "complete"));

            using var check = Fresh();
            var booking = await check.TutorBookings.AsNoTracking().FirstAsync(x => x.Id == id);
            var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(Parent, refund.OwnerId);
            Assert.Equal(WalletOwnerTypes.Parent, refund.OwnerType);
            Assert.Equal(9000 - booking.PaymentFeeXaf!.Value, refund.Amount);
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.OwnerId == Child));
            Assert.Equal("cancelled", booking.Status);
        }

        [Fact]
        public async Task InstitutionPaysByMobileMoney_TutorDeclines_FullRefundToHolderWallet()
        {
            var id = await SeedMobileMoneyBookingAsync("TBK-9005-1", TutorPayerTypes.Institution, InstHolder, Pupil, InstitutionId);
            Assert.True(await WebhookAsync("TBK-9005-1", "complete"));
            using (var db = Fresh())
                await Bookings(db).DeclineBookingAsync(Tutor, id, null);

            using var check = Fresh();
            var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(InstHolder, refund.OwnerId);
            Assert.Equal(WalletOwnerTypes.Institution, refund.OwnerType);
            Assert.Equal(9000, refund.Amount); // refus du tuteur : intégral, frais retenus sur le tuteur
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.OwnerId == Pupil));
        }

        // ── Recharge du portefeuille institution ─────────────────────────

        private PaymentService Payments(ApplicationDbContext db)
        {
            var orders = new Mock<IOrderService>();
            orders.Setup(o => o.UpdateOrderStatusAsync(It.IsAny<int>(), It.IsAny<string>()))
                .Returns(async (int orderId, string status) =>
                {
                    var order = await db.Orders.FirstAsync(x => x.Id == orderId);
                    order.Status = status;
                    await db.SaveChangesAsync();
                    return order;
                });
            return new PaymentService(new PaymentRepository(db), orders.Object, new Mock<INotchPayService>().Object, new Mock<IUserService>().Object,
                _ntfy.Object, new Mock<IEmailService>().Object, new Mock<IAffiliateService>().Object, new Mock<ISubscriptionActivationService>().Object,
                db, NullLogger<PaymentService>.Instance, new WalletService(db, NullLogger<WalletService>.Instance), new Mock<IPromoCodeService>().Object);
        }

        [Fact]
        public async Task InstitutionTopUp_WebhookCredits_HolderWallet_Once_EvenAfterHolderChange()
        {
            // Recharge lancée par un compte de l'institution qui n'est pas titulaire.
            int topUpId, orderId;
            using (var db = Fresh())
            {
                var payments = new Mock<IPaymentService>();
                payments.Setup(p => p.InitiateNotchPayAsync(InstAccount, It.IsAny<InitiatePaymentRequest>()))
                    .ReturnsAsync(new InitiatePaymentResponse { PaymentId = 0, Status = "pending" });
                var owner = await InstitutionWallet.ResolveWalletOwnerAsync(db, InstAccount);
                Assert.Equal(InstHolder, owner);
                var start = await new WalletTopUpService(db, payments.Object, NullLogger<WalletTopUpService>.Instance)
                    .StartAsync(InstAccount, 5000, "mtn", "677000000", "rch-1", owner);
                topUpId = start.TopUpId;
                orderId = start.OrderId;
                Assert.Equal(InstHolder, (await db.WalletTopUps.FirstAsync(t => t.Id == topUpId)).UserId);
                Assert.Equal(InstAccount, (await db.Orders.FirstAsync(o => o.Id == orderId)).UserId);
                db.Payments.Add(new Payment { OrderId = orderId, UserId = InstAccount, Amount = 5000, Currency = "XAF", Status = "pending",
                    PaymentMethod = "notchpay", NotchpayReference = $"WP-{orderId}-1" });
                await db.SaveChangesAsync();
            }

            // Le titulaire change pendant que la recharge est en attente : elle suit le portefeuille.
            using (var db = Fresh())
                await new InstitutionWalletHolderService(db, new WalletService(db, NullLogger<WalletService>.Instance), _ntfy.Object,
                        NullLogger<InstitutionWalletHolderService>.Instance)
                    .ChangeHolderAsync(InstitutionId, InstAccount, 1, "Passation", "chg-1");

            var tx = new NotchPayWebhookTransaction { Reference = $"WP-{orderId}-1", Status = "complete", Amount = 5000, Currency = "XAF" };
            using (var db = Fresh()) Assert.True(await Payments(db).HandleNotchPayWebhookAsync($"WP-{orderId}-1", "payment.complete", tx));
            using (var db = Fresh()) await Payments(db).HandleNotchPayWebhookAsync($"WP-{orderId}-1", "payment.complete", tx); // rejeu

            using var check = Fresh();
            var recharge = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceType == "WalletTopUp" && t.SourceId == topUpId);
            Assert.Equal(InstAccount, recharge.OwnerId);
            Assert.Equal(WalletOwnerTypes.Institution, recharge.OwnerType);
            Assert.Equal(5000, recharge.Amount);
            Assert.Equal("completed", (await check.WalletTopUps.FirstAsync(t => t.Id == topUpId)).Status);
            Assert.Equal(5000, await new WalletService(check, NullLogger<WalletService>.Instance).GetAvailableAsync(InstAccount));
            Assert.Equal(0, await new WalletService(check, NullLogger<WalletService>.Instance).GetAvailableAsync(InstHolder));
        }
    }
}
