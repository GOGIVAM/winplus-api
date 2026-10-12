using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Arbitrages 17.15 : séance unique jamais payée (créneau libéré au bout de
    /// 2 h, encaissement tardif remboursé au payeur sans confirmer la séance)
    /// et séance jamais marquée effectuée (passage automatique 24 h après la
    /// fin, contestation ouverte ensuite, puis libération normale).
    /// </summary>
    public class TutorBookingAutomation1715Tests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"auto1715-{Guid.NewGuid()}";
        private readonly Mock<INtfyService> _ntfy = new();
        private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

        private const int Tutor = 9701, Parent = 9702, Child = 9703, Student = 9704;
        private const int TutorProfileId = 9750;

        public TutorBookingAutomation1715Tests()
        {
            using var db = Fresh();
            User U(int id, string role) => new() { Id = id, Email = $"a{id}@test.local", Role = role, IsActive = true };
            db.Users.AddRange(U(Tutor, "teacher"), U(Parent, "parent"), U(Child, "student"), U(Student, "student"));
            db.TutorProfiles.Add(new TutorProfile { Id = TutorProfileId, UserId = Tutor, IsActive = true });
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = Parent, StudentId = Child, Status = "accepted", InitiatedBy = Parent });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);

        private TutorBookingService Bookings(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorBookingService(db, new Mock<INotchPayService>().Object, _ntfy.Object, NullLogger<TutorBookingService>.Instance, wallet, Config, refunds);
        }

        private static int _seq;

        private async Task<int> SeedAsync(Action<TutorBooking> setup)
        {
            using var db = Fresh();
            var b = new TutorBooking
            {
                TutorProfileId = TutorProfileId, StudentUserId = Child, PayerType = TutorPayerTypes.Parent, PayerUserId = Parent, BookedByUserId = Parent,
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)), StartTime = new TimeSpan(10, 0, 0), EndTime = new TimeSpan(11, 30, 0),
                PriceXaf = 9000, Status = "pending_payment", PaymentStatus = "pending", PaymentMethod = TutorPaymentMethods.MobileMoney,
                NotchpayReference = $"TBK-97{Interlocked.Increment(ref _seq):000}-1", PhoneNumber = "677000000",
                Participants = new List<TutorBookingParticipant> { new() { StudentUserId = Child } },
            };
            setup(b);
            db.TutorBookings.Add(b);
            await db.SaveChangesAsync();
            return b.Id;
        }

        private async Task<TutorBooking> LoadAsync(int id)
        {
            using var db = Fresh();
            return await db.TutorBookings.AsNoTracking().FirstAsync(x => x.Id == id);
        }

        private void VerifyNotified(int userId, string title, Times times) =>
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{userId}", title, It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]?>(), userId, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), times);

        // ── Séance unique jamais payée ───────────────────────────────────

        [Fact]
        public async Task UnpaidMobileMoney_After2h_Cancelled_SlotFreed_Idempotent()
        {
            var now = DateTime.UtcNow;
            var stale = await SeedAsync(b => b.CreatedAt = now.AddHours(-2).AddMinutes(-1));
            var recent = await SeedAsync(b => b.CreatedAt = now.AddMinutes(-90));
            var monthly = await SeedAsync(b => { b.CreatedAt = now.AddHours(-5); b.TutorEngagementId = 999_999; b.ObjectType = TutorBookingObjectTypes.MonthlySession; });

            using (var db = Fresh()) Assert.Equal(1, await Bookings(db).CancelAbandonedPaymentsAsync(now));
            using (var db = Fresh()) Assert.Equal(0, await Bookings(db).CancelAbandonedPaymentsAsync(now)); // rejeu de la tâche

            var expired = await LoadAsync(stale);
            Assert.Equal("cancelled", expired.Status);
            Assert.Equal(TutorBookingService.AbandonedPaymentReason, expired.CancellationReason);
            Assert.Equal("pending", expired.PaymentStatus);
            Assert.Null(expired.RefundedAt);
            Assert.Equal("pending_payment", (await LoadAsync(recent)).Status);
            // Séance d'engagement mensuel : c'est l'engagement entier qui est annulé (TutorEngagementService).
            Assert.Equal("pending_payment", (await LoadAsync(monthly)).Status);
            VerifyNotified(Parent, "Paiement non confirmé - séance annulée", Times.Once());

            using var check = Fresh();
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.SourceType == "TutorBooking" && t.SourceId == stale));
        }

        [Fact]
        public async Task LatePaymentAfterExpiration_RefundedToPayerWallet_NeverConfirmed_Idempotent()
        {
            var now = DateTime.UtcNow;
            string reference = "";
            var id = await SeedAsync(b => { b.CreatedAt = now.AddHours(-3); reference = b.NotchpayReference!; });
            using (var db = Fresh()) await Bookings(db).CancelAbandonedPaymentsAsync(now);

            var tx = new NotchPayWebhookTransaction { Reference = reference, Status = "complete", Amount = 9000, Currency = "XAF" };
            using (var db = Fresh()) Assert.True(await Bookings(db).TryHandleNotchPayWebhookAsync(reference, "payment.complete", tx));
            using (var db = Fresh()) Assert.True(await Bookings(db).TryHandleNotchPayWebhookAsync(reference, "payment.complete", tx)); // rejeu NotchPay

            var booking = await LoadAsync(id);
            Assert.Equal("cancelled", booking.Status);
            Assert.Null(booking.ConfirmedAt);
            Assert.Equal("payer_cancelled", booking.RefundCause);
            Assert.True(booking.PaymentFeeXaf > 0);

            using var check = Fresh();
            var refund = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRefund);
            Assert.Equal(Parent, refund.OwnerId);
            Assert.Equal(WalletOwnerTypes.Parent, refund.OwnerType);
            Assert.Equal(9000 - booking.PaymentFeeXaf!.Value, refund.Amount);
            var wallet = new WalletService(check, NullLogger<WalletService>.Instance);
            Assert.Equal(9000 - booking.PaymentFeeXaf.Value, await wallet.GetAvailableAsync(Parent));
            Assert.Equal(0, await wallet.GetAvailableAsync(Tutor));
            Assert.Equal(0, await wallet.GetAvailableAsync(Child));
            // Aucun revenu tuteur vivant : la séance ne reprend jamais vie.
            Assert.False(await check.WalletTransactions.AnyAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRevenue
                && t.Status != WalletEntryStatus.Reversed));
            VerifyNotified(Tutor, "Nouvelle demande de réservation", Times.Never());
            VerifyNotified(Parent, "Réservation annulée avant la réception du paiement", Times.Once());
        }

        // ── Séance jamais marquée effectuée ──────────────────────────────

        private static void EndedAt(TutorBooking b, DateTime endUtc)
        {
            b.SessionDate = DateOnly.FromDateTime(endUtc);
            b.EndTime = endUtc.TimeOfDay;
            b.StartTime = endUtc.TimeOfDay - TimeSpan.FromMinutes(60);
        }

        private static Action<TutorBooking> Confirmed(DateTime endUtc, Action<TutorBooking>? more = null) => b =>
        {
            // Fin de séance arrondie à la minute, toujours après 01:00 (début le même jour).
            var end = new DateTime(endUtc.Year, endUtc.Month, endUtc.Day, Math.Max(endUtc.Hour, 1), endUtc.Minute, 0, DateTimeKind.Utc);
            EndedAt(b, end);
            b.Status = "confirmed";
            b.PaymentStatus = "completed";
            b.PaymentFeeXaf = 180;
            b.ConfirmedAt = end.AddDays(-2);
            more?.Invoke(b);
        };

        [Fact]
        public async Task ConfirmedNotMarked_24hAfterEnd_AutoCompleted_OthersUntouched_Idempotent()
        {
            var now = new DateTime(2026, 10, 12, 15, 0, 0, DateTimeKind.Utc);
            var due = await SeedAsync(Confirmed(now.AddHours(-24).AddMinutes(-1)));
            var monthly = await SeedAsync(Confirmed(now.AddHours(-30), b => { b.TutorEngagementId = 999_998; b.ObjectType = TutorBookingObjectTypes.MonthlySession; }));
            var notYet = await SeedAsync(Confirmed(now.AddHours(-23)));
            var cancelled = await SeedAsync(Confirmed(now.AddHours(-48), b => b.Status = "cancelled"));
            var disputed = await SeedAsync(Confirmed(now.AddHours(-48), b => { b.Status = "disputed"; b.CompletedAt = now.AddHours(-40); }));
            var marked = await SeedAsync(Confirmed(now.AddHours(-48), b => { b.Status = "completed"; b.CompletedAt = now.AddHours(-47); }));

            using (var db = Fresh()) Assert.Equal(2, await Bookings(db).AutoCompleteOverdueAsync(now));
            using (var db = Fresh()) Assert.Equal(0, await Bookings(db).AutoCompleteOverdueAsync(now)); // rejeu de la tâche

            foreach (var id in new[] { due, monthly })
            {
                var b = await LoadAsync(id);
                Assert.Equal("completed", b.Status);
                Assert.Equal(now, b.CompletedAt); // la fenêtre de contestation court à partir du passage
                Assert.Null(b.EscrowReleasedAt);
            }
            Assert.Equal("confirmed", (await LoadAsync(notYet)).Status);
            Assert.Equal("cancelled", (await LoadAsync(cancelled)).Status);
            Assert.Equal("disputed", (await LoadAsync(disputed)).Status);
            Assert.Equal(now.AddHours(-47), (await LoadAsync(marked)).CompletedAt);

            VerifyNotified(Tutor, "Séance marquée effectuée automatiquement", Times.Exactly(2));
            VerifyNotified(Parent, "Séance considérée comme effectuée", Times.Exactly(2));
            VerifyNotified(Child, "Séance considérée comme effectuée", Times.Exactly(2));
        }

        [Fact]
        public async Task AutoCompleted_DisputeWindowOpensThen_PayerCanStillDispute()
        {
            var now = DateTime.UtcNow;
            var id = await SeedAsync(Confirmed(now.AddHours(-26)));
            using (var db = Fresh()) Assert.Equal(1, await Bookings(db).AutoCompleteOverdueAsync(now));

            TutorBookingDto dto;
            using (var db = Fresh()) dto = await Bookings(db).GetByIdAsync(Parent, id);
            Assert.True(dto.CanDispute);
            Assert.Equal(now.AddHours(24), dto.DisputeDeadline);

            // Libération tentée tout de suite : rien n'est versé pendant la fenêtre.
            await ReleaseEscrowAsync();
            Assert.Null((await LoadAsync(id)).EscrowReleasedAt);

            using (var db = Fresh()) await Bookings(db).DisputeBookingAsync(Parent, id, "La séance n'a pas eu lieu");
            Assert.Equal("disputed", (await LoadAsync(id)).Status);
        }

        [Fact]
        public async Task AutoCompleted_NoDispute_ReleasedAfterWindow_TutorPaid()
        {
            // Passage automatique daté d'il y a 25 h : la fenêtre de 24 h est écoulée.
            var completedAt = DateTime.UtcNow.AddHours(-25);
            var id = await SeedAsync(Confirmed(completedAt.AddHours(-25)));
            using (var db = Fresh())
            {
                await Bookings(db).AutoCompleteOverdueAsync(completedAt);
                await new WalletService(db, NullLogger<WalletService>.Instance).SyncTutorBookingAsync(id); // revenu en attente (encaissement)
            }

            await ReleaseEscrowAsync();
            await ReleaseEscrowAsync(); // rejeu : aucune double libération

            var booking = await LoadAsync(id);
            Assert.Equal("completed", booking.Status);
            Assert.NotNull(booking.EscrowReleasedAt);
            using var check = Fresh();
            var revenue = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceId == id && t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(WalletEntryStatus.Confirmed, revenue.Status);
            Assert.Equal(Tutor, revenue.OwnerId);
            Assert.True(revenue.Amount > 0);
            Assert.Equal(revenue.Amount, await new WalletService(check, NullLogger<WalletService>.Instance).GetAvailableAsync(Tutor));
            VerifyNotified(Tutor, "Fonds libérés", Times.Once());
        }

        /// <summary>Exécute la vraie libération de TutorBookingLifecycleService (même approche que TutorEngagementTests).</summary>
        private async Task ReleaseEscrowAsync()
        {
            var services = new ServiceCollection();
            services.AddScoped<ApplicationDbContext>(_ => Fresh());
            services.AddScoped<IWalletService>(sp => new WalletService(sp.GetRequiredService<ApplicationDbContext>(), NullLogger<WalletService>.Instance));
            services.AddSingleton(_ntfy.Object);
            await using var provider = services.BuildServiceProvider();
            var lifecycle = new TutorBookingLifecycleService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TutorBookingLifecycleService>.Instance, Config);
            var release = typeof(TutorBookingLifecycleService).GetMethod("ReleaseEscrowAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)release.Invoke(lifecycle, new object[] { CancellationToken.None })!;
        }
    }
}
