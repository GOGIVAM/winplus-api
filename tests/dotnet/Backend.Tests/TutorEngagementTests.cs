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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>Règles pures de l'engagement mensuel (lot 9.4) : période, remise, répartition des frais, créneaux.</summary>
    public class TutorMonthlyPlanTests
    {
        private static readonly DateOnly Monday = new(2026, 11, 2);

        [Fact]
        public void TwoWeeklySlots_GiveEightSessions_OverFourConsecutiveWeeks()
        {
            var slots = TutorMonthlyPlan.ParseSlots(new[]
            {
                new TutorEngagementSlotDto { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 60 },
                new TutorEngagementSlotDto { DayOfWeek = 4, StartTime = "17:00", DurationMinutes = 90 },
            }, 360);
            var occ = TutorMonthlyPlan.Occurrences(Monday, slots);
            Assert.Equal(8, occ.Count);
            Assert.All(occ, o => Assert.InRange(o.Date, Monday, TutorMonthlyPlan.EndDate(Monday)));
            Assert.Equal(4, occ.Count(o => o.Date.DayOfWeek == DayOfWeek.Tuesday && o.Start == new TimeSpan(18, 0, 0)));
            Assert.Equal(4, occ.Count(o => o.Date.DayOfWeek == DayOfWeek.Thursday && o.DurationMinutes == 90));
            Assert.Equal(Monday.AddDays(28), TutorMonthlyPlan.NextStartDate(Monday));
            Assert.Equal(Monday.AddDays(25).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc), TutorMonthlyPlan.AutoRenewAt(Monday));
        }

        [Theory]
        [InlineData(6000, 10, 5400)]
        [InlineData(9000, 10, 8100)]
        [InlineData(7500, 15, 6375)]
        [InlineData(6000, 45, 4200)] // bornée à 30 %
        [InlineData(6000, -5, 6000)]
        public void MonthlyDiscount_AppliesPerSession_ClampedToThirtyPercent(decimal price, int percent, decimal expected) =>
            Assert.Equal(expected, TutorMonthlyPlan.DiscountedPrice(price, percent));

        [Fact]
        public void FeeAllocation_SumsExactlyToThePaymentFee()
        {
            var prices = new List<decimal> { 5400, 8100, 5400, 8100, 5400, 8100, 5400, 8100 };
            var shares = TutorMonthlyPlan.AllocateFee(1620, prices);
            Assert.Equal(1620, shares.Sum());
            Assert.All(shares.Zip(prices), p => Assert.InRange(p.First, 0, p.Second));
            var odd = TutorMonthlyPlan.AllocateFee(101, new List<decimal> { 333, 333, 334 });
            Assert.Equal(101, odd.Sum());
        }

        [Fact]
        public void Slots_Overlapping_Misaligned_OrEmpty_AreRefused()
        {
            Assert.Throws<InvalidOperationException>(() => TutorMonthlyPlan.ParseSlots(new List<TutorEngagementSlotDto>(), 360));
            Assert.Throws<InvalidOperationException>(() => TutorMonthlyPlan.ParseSlots(new[]
            {
                new TutorEngagementSlotDto { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 90 },
                new TutorEngagementSlotDto { DayOfWeek = 2, StartTime = "19:00", DurationMinutes = 60 },
            }, 360));
            Assert.Throws<InvalidOperationException>(() => TutorMonthlyPlan.ParseSlots(new[]
            {
                new TutorEngagementSlotDto { DayOfWeek = 2, StartTime = "18:15", DurationMinutes = 60 },
            }, 360));
            Assert.Throws<InvalidOperationException>(() => TutorMonthlyPlan.ParseSlots(new[]
            {
                new TutorEngagementSlotDto { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 45 },
            }, 360));
        }
    }

    /// <summary>
    /// Engagement mensuel de bout en bout sur un vrai PostgreSQL (lot 9.4) :
    /// devis avec remise, conflit, paiement par solde, refus et silence du
    /// tuteur, annulation en cours de mois, libération séance par séance,
    /// renouvellement automatique réussi et refusé. Chaque test vérifie que le
    /// registre reste juste : débit du payeur = libérations + remboursements
    /// + commissions (somme des écritures non annulées de l'engagement = 0).
    /// </summary>
    public class TutorEngagementPostgresIntegrationTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        private readonly Mock<INtfyService> _ntfy = new();
        private readonly Mock<INotchPayService> _notchPay = new(MockBehavior.Strict);
        private static readonly IConfiguration Config = new ConfigurationBuilder().Build();

        public TutorEngagementPostgresIntegrationTests(PostgresTestDatabase pg) => _pg = pg;

        private sealed record World(int Tutor, int Student, int Parent, int Child, DateOnly Start);

        private async Task<World> SeedAsync(int discount = 10)
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            await using var db = _pg.CreateContext();
            User U(string name, string role) => new() { Email = $"{name}-{tag}@it.local", Role = role, IsActive = true, FirstName = name, LastName = "IT" };
            var tutor = U("tuteur", "teacher");
            var student = U("eleve", "student");
            var parent = U("parent", "parent");
            var child = U("enfant", "student");
            db.Users.AddRange(tutor, student, parent, child);
            await db.SaveChangesAsync();
            db.TutorProfiles.Add(new TutorProfile
            {
                UserId = tutor.Id, IsActive = true, HourlyRateXaf = 6000, OffersOnline = true, NoticeHours = 12,
                Subjects = new List<TutorSubject> { new() { Subject = "Maths" } },
                AvailabilitySlots = Enumerable.Range(0, 7)
                    .Select(d => new TutorAvailabilitySlot { DayOfWeek = d, StartTime = new TimeSpan(8, 0, 0), EndTime = new TimeSpan(20, 0, 0) }).ToList(),
                Packages = new List<TutorPackage> { new() { Name = "Engagement mensuel", Kind = TutorPackageKinds.Monthly, IsActive = true, DiscountPercent = discount } },
            });
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted", InitiatedBy = parent.Id });
            await db.SaveChangesAsync();
            // Lundi au moins 7 jours plus tard : toutes les séances respectent le préavis de 12 h.
            var start = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(7));
            while (start.DayOfWeek != DayOfWeek.Monday) start = start.AddDays(1);
            return new World(tutor.Id, student.Id, parent.Id, child.Id, start);
        }

        private async Task CreditAsync(int owner, decimal amount)
        {
            await using var db = _pg.CreateContext();
            await new WalletService(db, NullLogger<WalletService>.Instance)
                .PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"it-seed-{owner}-{Guid.NewGuid()}", "Recharge de test"));
        }

        private async Task<decimal> AvailableAsync(int owner)
        {
            await using var db = _pg.CreateContext();
            return await new WalletService(db, NullLogger<WalletService>.Instance).GetAvailableAsync(owner);
        }

        private TutorEngagementService Engagements(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorEngagementService(db, _notchPay.Object, _ntfy.Object, NullLogger<TutorEngagementService>.Instance, wallet, Config, refunds);
        }

        private TutorBookingService Bookings(ApplicationDbContext db)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var refunds = new TutorRefundService(db, wallet, _ntfy.Object, NullLogger<TutorRefundService>.Instance, Config);
            return new TutorBookingService(db, _notchPay.Object, _ntfy.Object, NullLogger<TutorBookingService>.Instance, wallet, Config, refunds);
        }

        /// <summary>Mardi 18:00 (1 h) et jeudi 17:00 (1 h 30) : 6 000 et 9 000 XAF par la grille, 5 400 et 8 100 après 10 %.</summary>
        private static CreateTutorEngagementRequestDto Request(World w, bool autoRenew = false) => new()
        {
            TutorUserId = w.Tutor, StartDate = w.Start, Mode = "online", Subject = "Maths",
            Slots = new List<TutorEngagementSlotDto>
            {
                new() { DayOfWeek = 2, StartTime = "18:00", DurationMinutes = 60 },
                new() { DayOfWeek = 4, StartTime = "17:00", DurationMinutes = 90 },
            },
            PaymentMethod = TutorPaymentMethods.Wallet, AutoRenew = autoRenew,
        };

        private async Task<int> CreateAcceptedAsync(World w, bool autoRenew = false)
        {
            int id;
            await using (var db = _pg.CreateContext())
                id = (await Engagements(db).CreateAsync(w.Student, Request(w, autoRenew))).Engagement.Id;
            await using (var db = _pg.CreateContext())
                await Engagements(db).AcceptAsync(w.Tutor, id);
            return id;
        }

        /// <summary>Somme des écritures non annulées liées à l'engagement (paiement + séances, tous propriétaires).</summary>
        private async Task<decimal> LedgerBalanceAsync(int engagementId)
        {
            await using var db = _pg.CreateContext();
            var sessionIds = await db.TutorBookings.Where(b => b.TutorEngagementId == engagementId).Select(b => b.Id).ToListAsync();
            return await db.WalletTransactions.AsNoTracking()
                .Where(t => t.Status != WalletEntryStatus.Reversed
                         && ((t.SourceType == "TutorEngagement" && t.SourceId == engagementId)
                             || (t.SourceType == "TutorBooking" && t.SourceId != null && sessionIds.Contains(t.SourceId.Value))))
                .SumAsync(t => t.Amount);
        }

        [PostgresFact]
        public async Task Quote_DetailsEachSession_AppliesMonthlyDiscount()
        {
            var w = await SeedAsync();
            await using var db = _pg.CreateContext();
            var q = await Engagements(db).GetQuoteAsync(Request(w));
            Assert.True(q.MonthlyEnabled);
            Assert.True(q.IsAvailable);
            Assert.Equal(8, q.Sessions.Count);
            Assert.Equal(60000, q.SubtotalXaf);
            Assert.Equal(10, q.DiscountPercent);
            Assert.Equal(6000, q.DiscountXaf);
            Assert.Equal(54000, q.TotalXaf);
            Assert.Equal(q.TotalXaf, q.Sessions.Sum(s => s.PriceXaf));
            Assert.Equal(4, q.Sessions.Count(s => s.PriceXaf == 5400));
            Assert.Equal(4, q.Sessions.Count(s => s.PriceXaf == 8100));
        }

        [PostgresFact]
        public async Task Create_WithOneConflict_IsRefusedWithTheList_NothingWritten()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 100000);
            await CreditAsync(w.Parent, 10000);
            // Le jeudi de la 2e semaine à 17:30 est déjà pris par une séance unique.
            var thursday = w.Start.AddDays(10);
            await using (var db = _pg.CreateContext())
            {
                var req = new CreateTutorBookingRequestDto
                {
                    TutorUserId = w.Tutor, SessionDate = thursday, StartTime = "17:30", DurationMinutes = 30, Mode = "online",
                    PaymentMethod = TutorPaymentMethods.Wallet, Subject = "Maths", PayerType = TutorPayerTypes.Parent, ParticipantUserIds = new List<int> { w.Child },
                };
                await Bookings(db).CreateBookingAsync(w.Parent, req);
            }

            await using (var db = _pg.CreateContext())
            {
                var ex = await Assert.ThrowsAsync<TutorEngagementConflictException>(() => Engagements(db).CreateAsync(w.Student, Request(w)));
                var conflict = Assert.Single(ex.Conflicts);
                Assert.Contains(thursday.ToString("dd/MM"), conflict);
            }
            await using var check = _pg.CreateContext();
            Assert.False(await check.TutorEngagements.AnyAsync(e => e.PayerUserId == w.Student));
            Assert.Equal(100000, await AvailableAsync(w.Student));
        }

        [PostgresFact]
        public async Task WalletPayment_EndToEnd_SessionsBlocked_RevenuePendingPerSession()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 60000);
            int id;
            await using (var db = _pg.CreateContext())
            {
                var created = await Engagements(db).CreateAsync(w.Student, Request(w));
                Assert.Equal(TutorEngagementStatuses.PendingTutorApproval, created.Engagement.Status);
                Assert.Equal(54000, created.Engagement.TotalXaf);
                Assert.Equal(54000, created.Engagement.HeldXaf);
                Assert.Equal(8, created.Engagement.Sessions.Count);
                id = created.Engagement.Id;
            }
            Assert.Equal(6000, await AvailableAsync(w.Student));

            // Créneaux bloqués : une séance unique ne peut plus se poser dessus.
            await using (var db = _pg.CreateContext())
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).CreateBookingAsync(w.Student, new CreateTutorBookingRequestDto
                {
                    TutorUserId = w.Tutor, SessionDate = w.Start.AddDays(1), StartTime = "18:30", DurationMinutes = 30, Mode = "online",
                    PaymentMethod = TutorPaymentMethods.Wallet, Subject = "Maths",
                }));
            // Le tuteur ne répond pas séance par séance, mais à l'engagement entier.
            await using (var db = _pg.CreateContext())
            {
                var sessionId = await db.TutorBookings.Where(b => b.TutorEngagementId == id).Select(b => b.Id).FirstAsync();
                await Assert.ThrowsAsync<InvalidOperationException>(() => Bookings(db).ConfirmBookingAsync(w.Tutor, sessionId));
            }

            await using (var db = _pg.CreateContext())
            {
                var accepted = await Engagements(db).AcceptAsync(w.Tutor, id);
                Assert.Equal(TutorEngagementStatuses.Active, accepted.Status);
                Assert.All(accepted.Sessions, s => Assert.Equal("confirmed", s.Status));
            }

            await using var check = _pg.CreateContext();
            var payment = await check.WalletTransactions.AsNoTracking().SingleAsync(t => t.SourceType == "TutorEngagement" && t.SourceId == id);
            Assert.Equal(-54000, payment.Amount);
            Assert.Equal(WalletEntryTypes.TutoringPayment, payment.EntryType);
            var sessionIds = await check.TutorBookings.Where(b => b.TutorEngagementId == id).Select(b => b.Id).ToListAsync();
            var revenues = await check.WalletTransactions.AsNoTracking()
                .Where(t => t.EntryType == WalletEntryTypes.TutoringRevenue && t.SourceId != null && sessionIds.Contains(t.SourceId.Value)).ToListAsync();
            Assert.Equal(8, revenues.Count);
            Assert.All(revenues, r => Assert.Equal(WalletEntryStatus.Pending, r.Status));
            Assert.Equal(0, await LedgerBalanceAsync(id));
        }

        [PostgresFact]
        public async Task TutorDeclines_ParentFullyRefunded_SessionBySession()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Parent, 54000);
            int id;
            await using (var db = _pg.CreateContext())
            {
                var req = Request(w);
                req.PayerType = TutorPayerTypes.Parent;
                req.ParticipantUserIds = new List<int> { w.Child };
                id = (await Engagements(db).CreateAsync(w.Parent, req)).Engagement.Id;
            }
            Assert.Equal(0, await AvailableAsync(w.Parent));

            await using (var db = _pg.CreateContext())
            {
                var declined = await Engagements(db).DeclineAsync(w.Tutor, id, "Indisponible ce mois-ci");
                Assert.Equal(TutorEngagementStatuses.Rejected, declined.Status);
                Assert.Equal(54000, declined.RefundedXaf);
                Assert.Equal(0, declined.HeldXaf);
                Assert.All(declined.Sessions, s => Assert.Equal("refunded", s.MoneyState));
            }
            Assert.Equal(54000, await AvailableAsync(w.Parent));
            Assert.Equal(0, await AvailableAsync(w.Child));
            Assert.Equal(0, await LedgerBalanceAsync(id));
        }

        [PostgresFact]
        public async Task NoResponse_EngagementExpires_FullRefund()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 54000);
            int id;
            await using (var db = _pg.CreateContext())
                id = (await Engagements(db).CreateAsync(w.Student, Request(w))).Engagement.Id;
            await using (var db = _pg.CreateContext())
                // Base partagée par la classe : d'autres demandes en attente peuvent expirer aussi.
                Assert.True(await Engagements(db).ExpireStaleRequestsAsync(DateTime.UtcNow.AddHours(13)) >= 1);
            await using var check = _pg.CreateContext();
            Assert.Equal(TutorEngagementStatuses.Expired, (await check.TutorEngagements.SingleAsync(e => e.Id == id)).Status);
            Assert.Equal(54000, await AvailableAsync(w.Student));
            Assert.Equal(0, await LedgerBalanceAsync(id));
        }

        /// <summary>
        /// Annulation par le payeur au milieu du mois : séances 1-2 passées
        /// (dues), séance 3 dans 10 h (50 % selon la politique par défaut),
        /// séance 4 dans 1 h (0 %), séances 5-8 à plus de 24 h (100 %).
        /// </summary>
        [PostgresFact]
        public async Task MidMonthCancellation_ByPayer_RefundsEachFutureSessionByPolicy()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 54000);
            var id = await CreateAcceptedAsync(w);

            List<int> ids;
            await using (var db = _pg.CreateContext())
            {
                var sessions = await db.TutorBookings.Where(b => b.TutorEngagementId == id).OrderBy(b => b.SessionDate).ThenBy(b => b.StartTime).ToListAsync();
                ids = sessions.Select(s => s.Id).ToList();
                var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                Place(sessions[0], yesterday, new TimeSpan(9, 0, 0));
                Place(sessions[1], yesterday, new TimeSpan(11, 0, 0));
                PlaceAt(sessions[2], DateTime.UtcNow.AddHours(10));
                PlaceAt(sessions[3], DateTime.UtcNow.AddHours(1));
                await db.SaveChangesAsync();
            }

            await using (var db = _pg.CreateContext())
            {
                var cancelled = await Engagements(db).CancelAsync(w.Student, id, "Déménagement");
                Assert.Equal(TutorEngagementStatuses.Cancelled, cancelled.Status);
                Assert.Equal(2700 + 0 + 5400 + 8100 + 5400 + 8100, cancelled.RefundedXaf);
            }

            await using var check = _pg.CreateContext();
            var s = await check.TutorBookings.AsNoTracking().Where(b => b.TutorEngagementId == id).ToDictionaryAsync(b => b.Id);
            Assert.Equal("confirmed", s[ids[0]].Status); // passées : restent dues
            Assert.Equal("confirmed", s[ids[1]].Status);
            Assert.Equal(50, s[ids[2]].RefundPercent);
            Assert.Equal(2700, s[ids[2]].RefundAmountXaf);
            Assert.Equal(2700, s[ids[2]].TutorRetainedXaf);
            Assert.Equal(0, s[ids[3]].RefundPercent);
            Assert.Equal(8100, s[ids[3]].TutorRetainedXaf);
            foreach (var i in ids.Skip(4))
            {
                Assert.Equal(100, s[i].RefundPercent);
                Assert.Equal(s[i].PriceXaf, s[i].RefundAmountXaf);
            }
            Assert.Equal(29700, await AvailableAsync(w.Student));
            // Registre : 54 000 payés = 13 500 encore bloqués (séances passées) + 10 800 conservés au tuteur + 29 700 remboursés.
            Assert.Equal(0, await LedgerBalanceAsync(id));
        }

        [PostgresFact]
        public async Task MoneyReleasedSessionBySession_DisputeOnlyBlocksItsSession()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 54000);
            var id = await CreateAcceptedAsync(w);

            List<int> ids;
            await using (var db = _pg.CreateContext())
            {
                var sessions = await db.TutorBookings.Where(b => b.TutorEngagementId == id).OrderBy(b => b.SessionDate).ThenBy(b => b.StartTime).ToListAsync();
                ids = sessions.Select(x => x.Id).ToList();
                var yesterday = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-1));
                Place(sessions[0], yesterday, new TimeSpan(9, 0, 0));
                Place(sessions[1], yesterday, new TimeSpan(11, 0, 0));
                await db.SaveChangesAsync();
            }
            await using (var db = _pg.CreateContext())
            {
                await Bookings(db).CompleteBookingAsync(w.Tutor, ids[0]);
                await Bookings(db).CompleteBookingAsync(w.Tutor, ids[1]);
                await Bookings(db).DisputeBookingAsync(w.Student, ids[1], "Le tuteur n'est pas venu");
            }
            await using (var db = _pg.CreateContext())
            {
                foreach (var b in await db.TutorBookings.Where(b => b.Id == ids[0] || b.Id == ids[1]).ToListAsync())
                    b.CompletedAt = DateTime.UtcNow.AddHours(-25);
                await db.SaveChangesAsync();
            }

            await ReleaseEscrowAsync();

            await using (var db = _pg.CreateContext())
            {
                var e = await Engagements(db).GetByIdAsync(w.Student, id);
                var first = e.Sessions.Single(x => x.Id == ids[0]);
                var second = e.Sessions.Single(x => x.Id == ids[1]);
                Assert.Equal("released", first.MoneyState);
                Assert.Equal("disputed", second.Status);
                Assert.Equal("held", second.MoneyState);
                Assert.Equal(first.PriceXaf, e.ReleasedXaf);
                Assert.Equal(54000 - first.PriceXaf, e.HeldXaf);
                Assert.All(e.Sessions.Skip(2), x => Assert.Equal("confirmed", x.Status));
            }
            await using var check = _pg.CreateContext();
            var revenue = await check.WalletTransactions.AsNoTracking()
                .SingleAsync(t => t.SourceType == "TutorBooking" && t.SourceId == ids[0] && t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(WalletEntryStatus.Confirmed, revenue.Status);
            var disputed = await check.WalletTransactions.AsNoTracking()
                .SingleAsync(t => t.SourceType == "TutorBooking" && t.SourceId == ids[1] && t.EntryType == WalletEntryTypes.TutoringRevenue);
            Assert.Equal(WalletEntryStatus.Pending, disputed.Status);
            Assert.True(await AvailableAsync(w.Tutor) > 0);
            Assert.Equal(0, await LedgerBalanceAsync(id));
        }

        [PostgresFact]
        public async Task AutoRenewal_Succeeds_WithSufficientBalance()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 54000 * 2);
            var id = await CreateAcceptedAsync(w, autoRenew: true);
            var at = TutorMonthlyPlan.AutoRenewAt(w.Start).AddMinutes(5);

            await using (var db = _pg.CreateContext())
                Assert.True(await Engagements(db).SendRenewalRemindersAsync(TutorMonthlyPlan.RenewalReminderAt(w.Start).AddMinutes(1)) >= 1);
            await using (var db = _pg.CreateContext())
                Assert.True(await Engagements(db).RunAutoRenewalsAsync(at) >= 1);
            await using (var db = _pg.CreateContext())
                Assert.Equal(0, await Engagements(db).RunAutoRenewalsAsync(at)); // jamais deux tentatives

            await using var check = _pg.CreateContext();
            var old = await check.TutorEngagements.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.NotNull(old.RenewedToEngagementId);
            var renewed = await check.TutorEngagements.AsNoTracking().Include(e => e.Sessions).SingleAsync(e => e.Id == old.RenewedToEngagementId);
            Assert.Equal(w.Start.AddDays(28), renewed.StartDate);
            Assert.Equal(id, renewed.RenewedFromEngagementId);
            Assert.Equal(TutorEngagementStatuses.PendingTutorApproval, renewed.Status);
            Assert.True(renewed.AutoRenew);
            Assert.Equal(8, renewed.Sessions.Count);
            Assert.Equal(0, await AvailableAsync(w.Student));
            Assert.Equal(0, await LedgerBalanceAsync(renewed.Id));
        }

        [PostgresFact]
        public async Task AutoRenewal_Refused_WhenBalanceInsufficient_PayerNotified()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Student, 54000);
            var id = await CreateAcceptedAsync(w, autoRenew: true);

            await using (var db = _pg.CreateContext())
                Assert.Equal(0, await Engagements(db).RunAutoRenewalsAsync(TutorMonthlyPlan.AutoRenewAt(w.Start).AddMinutes(5)));

            await using var check = _pg.CreateContext();
            var old = await check.TutorEngagements.AsNoTracking().SingleAsync(e => e.Id == id);
            Assert.Null(old.RenewedToEngagementId);
            Assert.NotNull(old.AutoRenewAttemptedAt);
            Assert.StartsWith("Solde insuffisant", old.AutoRenewFailureReason);
            Assert.Equal(1, await check.TutorEngagements.CountAsync(e => e.PayerUserId == w.Student));
            Assert.Equal(0, await AvailableAsync(w.Student));
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{w.Student}", "Renouvellement automatique impossible", It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string[]?>(), w.Student, It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
        }

        private static void Place(TutorBooking b, DateOnly date, TimeSpan start)
        {
            var minutes = b.EndTime - b.StartTime;
            b.SessionDate = date;
            b.StartTime = start;
            b.EndTime = start + minutes;
        }

        private static void PlaceAt(TutorBooking b, DateTime utc)
        {
            var start = new TimeSpan(utc.Hour, utc.Minute >= 30 ? 30 : 0, 0);
            b.SessionDate = DateOnly.FromDateTime(utc);
            b.StartTime = start;
            b.EndTime = start + TimeSpan.FromMinutes(30);
        }

        /// <summary>Exécute la vraie libération de TutorBookingLifecycleService (24 h après la séance effectuée, sans litige).</summary>
        private async Task ReleaseEscrowAsync()
        {
            var services = new ServiceCollection();
            services.AddScoped<ApplicationDbContext>(_ => _pg.CreateContext());
            services.AddScoped<IWalletService>(sp => new WalletService(sp.GetRequiredService<ApplicationDbContext>(), NullLogger<WalletService>.Instance));
            services.AddSingleton(_ntfy.Object);
            await using var provider = services.BuildServiceProvider();
            var lifecycle = new TutorBookingLifecycleService(provider.GetRequiredService<IServiceScopeFactory>(), NullLogger<TutorBookingLifecycleService>.Instance, Config);
            var release = typeof(TutorBookingLifecycleService).GetMethod("ReleaseEscrowAsync", BindingFlags.NonPublic | BindingFlags.Instance)!;
            await (Task)release.Invoke(lifecycle, new object[] { CancellationToken.None })!;
        }
    }
}
