using System;
using System.Linq;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 2, Module 1 : journal de portefeuille. Vrai WalletService sur le vrai
    /// modèle EF (fournisseur InMemory, voir TestDb). La sérialisation des
    /// débits y est assurée par le verrou de repli propre au processus ; en
    /// production, par le verrou consultatif PostgreSQL de la transaction.
    /// </summary>
    public class WalletLedgerTests
    {
        // Racine partagée (une par classe, pas par test) : EF construit un
        // fournisseur de services interne par racine distincte, et en refuse
        // plus de vingt. L'isolement entre tests vient du nom de base unique.
        private static readonly InMemoryDatabaseRoot _root = new();
        private readonly string _dbName = $"wallet-{Guid.NewGuid()}";

        // Identifiants propres à cette classe : le verrou de repli est statique,
        // indexé par utilisateur.
        private const int Teacher = 9101;
        private const int Buyer = 9102;
        private const int Affiliate = 9103;

        public WalletLedgerTests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = Teacher, Email = "prof@test.local", Role = "teacher" },
                new User { Id = Buyer, Email = "acheteur@test.local", Role = "teacher" },
                new User { Id = Affiliate, Email = "affilie@test.local", Role = "teacher" });
            db.Subjects.Add(new Subject { Id = 501, Title = "Annales Maths", Price = 2000, AuthorUserId = Teacher });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, _root);

        private static WalletService Wallet(ApplicationDbContext db) => new(db, NullLogger<WalletService>.Instance);

        private static WalletEntry Credit(int owner, decimal amount, string key) =>
            new(owner, WalletEntryTypes.AdminCredit, amount, key, "Crédit de test", "AdminCredit");

        // ── Solde et idempotence ─────────────────────────────────────────

        [Fact]
        public async Task Balance_IsSumOfConfirmedEntries_PendingShownSeparately()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "t1"));
            await wallet.PostAsync(new WalletEntry(Teacher, WalletEntryTypes.TutoringRevenue, 800, "t2", "Séance",
                "TutorBooking", 1, WalletEntryStatus.Pending));

            var balance = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(1000, balance.AvailableXaf);
            Assert.Equal(800, balance.PendingXaf);
            Assert.False(balance.IsAnomaly);
        }

        [Fact]
        public async Task ReplayedEvent_CreatesSingleEntry()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            var first = await wallet.PostAsync(Credit(Teacher, 1000, "replay"));
            var second = await wallet.PostAsync(Credit(Teacher, 1000, "replay"));

            Assert.Equal(first.Id, second.Id);
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.IdempotencyKey == "replay"));
            Assert.Equal(1000, await wallet.GetAvailableAsync(Teacher));
        }

        [Fact]
        public async Task Amounts_AreRoundedToWholeXaf()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            var entry = await wallet.PostAsync(Credit(Teacher, 1500.5m, "round"));
            Assert.Equal(1501, entry.Amount);
        }

        // ── Débit et double dépense ──────────────────────────────────────

        [Fact]
        public async Task Debit_ExactBalancePasses_OneMoreUnitIsRefused()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));

            var refused = await Assert.ThrowsAsync<InsufficientWalletBalanceException>(() =>
                wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.WithdrawalRequested, 1001, "d1", "Retrait"))));
            Assert.Equal(1000, refused.AvailableXaf);
            Assert.Equal(1001, refused.RequiredXaf);

            await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.WithdrawalRequested, 1000, "d2", "Retrait")));
            Assert.Equal(0, await wallet.GetAvailableAsync(Teacher));
        }

        [Fact]
        public async Task SequentialDebits_CannotSpendTheSameBalanceTwice()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));

            await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.BalancePurchase, 700, "p1", "Achat")));
            await Assert.ThrowsAsync<InsufficientWalletBalanceException>(() =>
                wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.BalancePurchase, 700, "p2", "Achat"))));

            Assert.Equal(300, await wallet.GetAvailableAsync(Teacher));
        }

        [Fact]
        public async Task ConcurrentDebits_OnlyOneSucceeds_WhenBalanceCoversOne()
        {
            using (var seed = Fresh())
                await Wallet(seed).PostAsync(Credit(Buyer, 1000, "c-concurrent"));

            // Deux requêtes, deux contextes (deux scopes DI), même base.
            async Task<bool> Attempt(string key)
            {
                using var db = Fresh();
                var wallet = Wallet(db);
                try
                {
                    await wallet.RunLockedAsync(Buyer, async () =>
                    {
                        var available = await wallet.GetAvailableAsync(Buyer);
                        await Task.Delay(50); // élargit la fenêtre de course
                        return await wallet.DebitAsync(new WalletEntry(Buyer, WalletEntryTypes.WithdrawalRequested, 600, key, "Retrait"));
                    });
                    return true;
                }
                catch (InsufficientWalletBalanceException) { return false; }
            }

            var results = await Task.WhenAll(Attempt("w-a"), Attempt("w-b"));

            Assert.Equal(1, results.Count(ok => ok));
            using var check = Fresh();
            Assert.Equal(400, await Wallet(check).GetAvailableAsync(Buyer));
        }

        [Fact]
        public async Task DoubleSubmitOfSameDebit_DebitsOnce()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));
            var debit = new WalletEntry(Teacher, WalletEntryTypes.WithdrawalRequested, 600, "same-click", "Retrait");

            await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(debit));
            await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(debit));

            Assert.Equal(400, await wallet.GetAvailableAsync(Teacher));
        }

        // ── Contre-passation ─────────────────────────────────────────────

        [Fact]
        public async Task Reverse_RestoresBalance_OnceOnly_AndKeepsOriginal()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));
            var debit = await wallet.RunLockedAsync(Teacher, () =>
                wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.WithdrawalRequested, 1000, "w", "Retrait")));

            var r1 = await wallet.ReverseAsync(debit.Id, "Échec du transfert");
            var r2 = await wallet.ReverseAsync(debit.Id, "Échec du transfert");

            Assert.NotNull(r1);
            Assert.Equal(r1!.Id, r2!.Id);
            Assert.Equal(1000, await wallet.GetAvailableAsync(Teacher));
            Assert.Equal(-1000, (await db.WalletTransactions.AsNoTracking().FirstAsync(t => t.Id == debit.Id)).Amount);
        }

        // ── Traduction des événements métier ─────────────────────────────

        private static Order PaidOrder(int id, int userId, string status = "completed", string method = "mtn") => new()
        {
            Id = id, UserId = userId, OrderNumber = $"WP-{id}", TotalAmount = 2000, Status = status, PaymentMethod = method,
        };

        [Fact]
        public async Task PaidOrder_CreditsAuthor_ReplayIsIdempotent_RefundReverses()
        {
            using var db = Fresh();
            db.Orders.Add(PaidOrder(701, Buyer));
            db.OrderItems.Add(new OrderItem { Id = 801, OrderId = 701, SubjectId = 501, PriceAtPurchase = 2000 });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);

            await wallet.SyncOrderAsync(701);
            await wallet.SyncOrderAsync(701);
            Assert.Equal(2000, await wallet.GetAvailableAsync(Teacher));
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.CatalogSale));

            var order = await db.Orders.FirstAsync(o => o.Id == 701);
            order.Status = "refunded";
            await db.SaveChangesAsync();
            await wallet.SyncOrderAsync(701);
            await wallet.SyncOrderAsync(701);

            Assert.Equal(0, await wallet.GetAvailableAsync(Teacher));
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.Reversal));
            // Aucune commission plateforme sur le catalogue avant le Module 7.
            Assert.Equal(0, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.PlatformCommission));
        }

        [Fact]
        public async Task RefundedBalancePurchase_RestoresBuyerDebit()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Buyer, 5000, "c-buyer"));
            db.Orders.Add(PaidOrder(702, Buyer, method: "balance"));
            await db.SaveChangesAsync();
            await wallet.RunLockedAsync(Buyer, () => wallet.DebitAsync(new WalletEntry(Buyer, WalletEntryTypes.BalancePurchase, 2000,
                WalletService.BalancePurchaseKey(702), "Achat panier WP-702", "Order", 702)));
            Assert.Equal(3000, await wallet.GetAvailableAsync(Buyer));

            (await db.Orders.FirstAsync(o => o.Id == 702)).Status = "refunded";
            await db.SaveChangesAsync();
            await wallet.SyncOrderAsync(702);

            Assert.Equal(5000, await wallet.GetAvailableAsync(Buyer));
        }

        [Fact]
        public async Task TutoringRevenue_PendingUntilEscrowRelease_ConfirmedOnce()
        {
            using var db = Fresh();
            db.TutorProfiles.Add(new TutorProfile { Id = 61, UserId = Teacher });
            db.TutorBookings.Add(new TutorBooking { Id = 62, TutorProfileId = 61, StudentUserId = Buyer, PriceXaf = 5000,
                Status = "confirmed", PaymentStatus = "completed", SessionDate = new DateOnly(2026, 10, 1) });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);

            await wallet.SyncTutorBookingAsync(62);
            var before = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(0, before.AvailableXaf);
            Assert.Equal(4000, before.PendingXaf); // part enseignant par défaut : 80 %

            (await db.TutorBookings.FirstAsync(b => b.Id == 62)).EscrowReleasedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            await wallet.SyncTutorBookingAsync(62);
            await wallet.SyncTutorBookingAsync(62); // libération rejouée

            var after = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(4000, after.AvailableXaf);
            Assert.Equal(0, after.PendingXaf);
            var platform = await db.WalletTransactions.Where(t => t.OwnerType == WalletOwnerTypes.Platform).ToListAsync();
            Assert.Single(platform);
            Assert.Equal(1000, platform[0].Amount);
            Assert.Equal(WalletEntryStatus.Confirmed, platform[0].Status);
        }

        [Fact]
        public async Task CancelledBooking_PendingRevenueIsVoided()
        {
            using var db = Fresh();
            db.TutorProfiles.Add(new TutorProfile { Id = 63, UserId = Teacher });
            db.TutorBookings.Add(new TutorBooking { Id = 64, TutorProfileId = 63, StudentUserId = Buyer, PriceXaf = 3000,
                Status = "pending_tutor_approval", PaymentStatus = "completed", SessionDate = new DateOnly(2026, 10, 1) });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);
            await wallet.SyncTutorBookingAsync(64);

            var booking = await db.TutorBookings.FirstAsync(b => b.Id == 64);
            booking.Status = "rejected";
            booking.PaymentStatus = "refunded";
            await db.SaveChangesAsync();
            await wallet.SyncTutorBookingAsync(64);

            var balance = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(0, balance.PendingXaf);
            Assert.Equal(0, balance.AvailableXaf);
            Assert.All(await db.WalletTransactions.Where(t => t.SourceId == 64).ToListAsync(),
                t => Assert.Equal(WalletEntryStatus.Reversed, t.Status));
        }

        [Fact]
        public async Task AffiliateCommission_PendingThenConfirmed_ReversedAfterMaturityGivesInverseEntry()
        {
            using var db = Fresh();
            db.Orders.Add(PaidOrder(703, Buyer));
            db.AffiliateAccounts.Add(new AffiliateAccount { Id = 71, UserId = Affiliate, Code = "AFF71" });
            db.AffiliateCommissions.Add(new AffiliateCommission { Id = 72, AffiliateAccountId = 71, OrderId = 703, CommissionAmount = 150, Status = "pending" });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);

            await wallet.SyncAffiliateCommissionAsync(72);
            Assert.Equal(150, (await wallet.GetBalanceAsync(Affiliate)).PendingXaf);

            var c = await db.AffiliateCommissions.FirstAsync(x => x.Id == 72);
            c.Status = "confirmed";
            await db.SaveChangesAsync();
            await wallet.SyncAffiliateCommissionAsync(72);
            Assert.Equal(150, await wallet.GetAvailableAsync(Affiliate));

            c.Status = "reversed";
            await db.SaveChangesAsync();
            await wallet.SyncAffiliateCommissionAsync(72);

            Assert.Equal(0, await wallet.GetAvailableAsync(Affiliate));
            var original = await db.WalletTransactions.FirstAsync(t => t.IdempotencyKey == WalletService.AffiliateKey(72));
            Assert.Equal(WalletEntryStatus.Confirmed, original.Status); // jamais repassée en attente
            Assert.True(await db.WalletTransactions.AnyAsync(t => t.ReversesEntryId == original.Id));
        }

        [Fact]
        public async Task EngagedAmount_TracksWithdrawalInProgress()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));
            await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.WithdrawalRequested, 600,
                WalletService.WithdrawalRequestedKey(1), "Retrait", "Withdrawal", 1)));

            var during = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(400, during.AvailableXaf);
            Assert.Equal(600, during.EngagedXaf);

            await wallet.PostAsync(new WalletEntry(Teacher, WalletEntryTypes.WithdrawalProcessed, 0,
                WalletService.WithdrawalProcessedKey(1), "Retrait versé", "Withdrawal", 1));
            var after = await wallet.GetBalanceAsync(Teacher);
            Assert.Equal(400, after.AvailableXaf);
            Assert.Equal(0, after.EngagedXaf);
        }

        [Fact]
        public async Task History_FiltersBySource_ReversalFollowsItsOrigin()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            await wallet.PostAsync(Credit(Teacher, 1000, "c"));
            var debit = await wallet.RunLockedAsync(Teacher, () => wallet.DebitAsync(new WalletEntry(Teacher, WalletEntryTypes.ClassAssignment, 300,
                WalletService.ClassAssignmentKey(5), "Assignation classe : X", "TeacherClassContent", 5)));
            await wallet.ReverseAsync(debit.Id, "Assignation retirée");

            var (purchases, total) = await wallet.GetHistoryAsync(Teacher, WalletSources.Purchase, 1, 50);
            Assert.Equal(2, total);
            Assert.All(purchases, p => Assert.Equal(WalletSources.Purchase, p.Source));

            var (admin, _) = await wallet.GetHistoryAsync(Teacher, WalletSources.AdminCredit, 1, 50);
            Assert.Single(admin);
        }

        // ── Reprise ──────────────────────────────────────────────────────

        [Fact]
        public async Task Backfill_LedgerBalanceEqualsLegacyBalance_AndIsRerunnable()
        {
            using var db = Fresh();
            // Historique « ancien modèle » : une vente, une séance libérée, une
            // commission confirmée, une assignation facturée, un achat par
            // solde, un retrait manuel en attente et un retrait échoué.
            db.Orders.Add(PaidOrder(710, Buyer));
            db.OrderItems.Add(new OrderItem { Id = 810, OrderId = 710, SubjectId = 501, PriceAtPurchase = 2000 });
            db.Orders.Add(new Order { Id = 711, UserId = Teacher, OrderNumber = "WP-711", TotalAmount = 500, Status = "completed", PaymentMethod = "balance" });
            db.TutorProfiles.Add(new TutorProfile { Id = 65, UserId = Teacher });
            db.TutorBookings.Add(new TutorBooking { Id = 66, TutorProfileId = 65, StudentUserId = Buyer, PriceXaf = 5000,
                Status = "completed", PaymentStatus = "completed", EscrowReleasedAt = DateTime.UtcNow.AddDays(-2), SessionDate = new DateOnly(2026, 9, 1) });
            db.AffiliateAccounts.Add(new AffiliateAccount { Id = 73, UserId = Teacher, Code = "AFF73" });
            db.AffiliateCommissions.Add(new AffiliateCommission { Id = 74, AffiliateAccountId = 73, OrderId = 710, CommissionAmount = 100, Status = "confirmed" });
            db.TeacherClasses.Add(new TeacherClass { Id = 91, TeacherId = Teacher, Name = "Tle C" });
            db.TeacherClassContents.Add(new TeacherClassContent { Id = 92, TeacherClassId = 91, SubjectId = 501, AssignedByUserId = Teacher, PriceChargedXaf = 300 });
            db.Withdrawals.Add(new Withdrawal { Id = 93, UserId = Teacher, Phone = "677000000", AmountXaf = 1000, Status = "pending" });
            db.Withdrawals.Add(new Withdrawal { Id = 94, UserId = Teacher, Phone = "677000000", AmountXaf = 999, Status = "failed" });
            await db.SaveChangesAsync();

            var wallet = Wallet(db);
            var backfill = new WalletBackfillService(db, wallet, NullLogger<WalletBackfillService>.Instance);

            var before = await backfill.CompareAsync();
            Assert.False(before.Equal); // journal vide : écart attendu avant reprise

            var report = await backfill.RunAsync();
            Assert.True(report.Equal, string.Join("; ", report.Mismatches.Select(m => $"{m.UserId}: {m.LegacyXaf} ≠ {m.LedgerXaf}")));
            // 2000 + 4000 (80 % de 5000) + 100 - 300 - 500 - 1000 = 4300
            Assert.Equal(4300, await wallet.GetAvailableAsync(Teacher));

            var count = await db.WalletTransactions.CountAsync();
            var again = await backfill.RunAsync();
            Assert.True(again.Equal);
            Assert.Equal(count, await db.WalletTransactions.CountAsync());
        }
    }
}
