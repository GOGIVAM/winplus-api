using System;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 2, Module 2 : retrait automatisé. Vrai WithdrawalService et vrai
    /// WalletService sur le modèle EF (InMemory) ; NotchPay et ntfy simulés.
    /// </summary>
    public class WithdrawalServiceTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"withdraw-{Guid.NewGuid()}";
        private readonly Mock<INotchPayService> _notchPay = new();
        private readonly Mock<INtfyService> _ntfy = new();

        // Identifiants propres à cette classe (le verrou de repli est statique, par utilisateur).
        private const int Prof = 9201;
        private const int Suspended = 9202;
        private const int Racer = 9203;
        private const int Admin = 9299;

        public WithdrawalServiceTests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = Prof, Email = "prof-w@test.local", Role = "teacher", FirstName = "Awa" },
                new User { Id = Suspended, Email = "susp@test.local", Role = "teacher", IsActive = false },
                new User { Id = Racer, Email = "racer@test.local", Role = "teacher" },
                new User { Id = Admin, Email = "admin-w@test.local", Role = "admin" });
            db.SaveChanges();

            _notchPay.Setup(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()))
                .ReturnsAsync((NotchPayTransferRequest r) => new NotchPayTransfer { Id = $"trf_{r.Reference}", Reference = r.Reference, Status = "pending" });
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);

        private (WithdrawalService Service, WalletService Wallet, ApplicationDbContext Db) Build()
        {
            var db = Fresh();
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var service = new WithdrawalService(db, wallet, _notchPay.Object, _ntfy.Object, NullLogger<WithdrawalService>.Instance);
            return (service, wallet, db);
        }

        private async Task Fund(int userId, decimal amount)
        {
            var (_, wallet, db) = Build();
            await wallet.PostAsync(new WalletEntry(userId, WalletEntryTypes.AdminCredit, amount, $"fund-{userId}-{Guid.NewGuid()}", "Crédit de test"));
            db.Dispose();
        }

        private void ProviderReports(string status, string? reason = null) =>
            _notchPay.Setup(n => n.GetTransferAsync(It.IsAny<string>()))
                .ReturnsAsync((string reference) => new NotchPayTransfer { Id = $"trf_{reference}", Reference = reference, Status = status, FailureReason = reason });

        // ── Règles serveur ───────────────────────────────────────────────

        [Theory]
        [InlineData(499, "below_minimum")]
        [InlineData(0, "below_minimum")]
        [InlineData(1000.5, "invalid_amount")]
        public async Task InvalidAmount_IsRefusedByServer_WithoutReservationNorTransfer(double amount, string code)
        {
            await Fund(Prof, 5000);
            var (service, wallet, _) = Build();

            var ex = await Assert.ThrowsAsync<WithdrawalRejectedException>(() =>
                service.RequestAsync(Prof, "mtn", "677000001", (decimal)amount, null));

            Assert.Equal(code, ex.Code);
            Assert.Equal(400, ex.HttpStatus);
            Assert.Equal(5000, await wallet.GetAvailableAsync(Prof));
            _notchPay.Verify(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()), Times.Never);
        }

        [Fact]
        public async Task ExactBalancePasses_NoCeiling_OneUnitMoreIsRefused()
        {
            await Fund(Prof, 25000);
            var (service, wallet, _) = Build();

            var over = await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RequestAsync(Prof, "mtn", "677000001", 25001, null));
            Assert.Equal(402, over.HttpStatus);
            Assert.Equal(25000, over.AvailableXaf);

            var ok = await service.RequestAsync(Prof, "orange", "+237 699 00 00 01", 25000, "c-1");
            Assert.Equal(WithdrawalStatus.Processing, ok.Withdrawal.Status);
            Assert.Equal("237699000001", ok.Withdrawal.Phone);
            Assert.Equal(0, await wallet.GetAvailableAsync(Prof));
            Assert.Equal(25000, (await wallet.GetBalanceAsync(Prof)).EngagedXaf);

            // Aucun frais : le montant transmis au fournisseur est le montant demandé, entier.
            _notchPay.Verify(n => n.CreateTransferAsync(It.Is<NotchPayTransferRequest>(r =>
                r.AmountXaf == 25000 && r.Channel == "cm.orange" && r.Reference == $"WDR-{ok.Withdrawal.Id}")), Times.Once);
        }

        [Fact]
        public async Task InvalidPhoneOrOperator_AndSuspendedAccount_AreRefused()
        {
            await Fund(Prof, 5000);
            var (service, _, _) = Build();
            Assert.Equal("invalid_phone", (await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RequestAsync(Prof, "mtn", "12345", 1000, null))).Code);
            Assert.Equal("invalid_operator", (await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RequestAsync(Prof, "wave", "677000001", 1000, null))).Code);

            await Fund(Suspended, 5000);
            var refused = await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RequestAsync(Suspended, "mtn", "677000001", 1000, null));
            Assert.Equal(403, refused.HttpStatus);
        }

        // ── Double soumission et concurrence ─────────────────────────────

        [Fact]
        public async Task SameClientRequestTwice_ProducesOneWithdrawalAndOneTransfer()
        {
            await Fund(Prof, 5000);
            var (service, wallet, db) = Build();

            var first = await service.RequestAsync(Prof, "mtn", "677000001", 1000, "same-form");
            var second = await service.RequestAsync(Prof, "mtn", "677000001", 1000, "same-form");

            Assert.False(first.Duplicate);
            Assert.True(second.Duplicate);
            Assert.Equal(first.Withdrawal.Id, second.Withdrawal.Id);
            Assert.Equal(1, await db.Withdrawals.CountAsync(w => w.UserId == Prof));
            Assert.Equal(4000, await wallet.GetAvailableAsync(Prof));
            _notchPay.Verify(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()), Times.Once);
        }

        [Fact]
        public async Task TwoSimultaneousWithdrawals_NeverExceedTheBalance()
        {
            await Fund(Racer, 1000);

            async Task<bool> Attempt(string requestId)
            {
                var (service, _, db) = Build();
                try { await service.RequestAsync(Racer, "mtn", "677000002", 600, requestId); return true; }
                catch (WithdrawalRejectedException ex) when (ex.HttpStatus == 402) { return false; }
                finally { db.Dispose(); }
            }

            var results = await Task.WhenAll(Attempt("r-a"), Attempt("r-b"), Attempt("r-c"));

            Assert.Equal(1, results.Count(ok => ok));
            var (_, wallet, check) = Build();
            Assert.Equal(400, await wallet.GetAvailableAsync(Racer));
            Assert.Equal(1, await check.Withdrawals.CountAsync(w => w.UserId == Racer));
        }

        // ── Issues du transfert ──────────────────────────────────────────

        [Fact]
        public async Task ProviderRejection_RestoresFundsExactly_AndNotifiesTeacherAndAdmin()
        {
            await Fund(Prof, 3000);
            _notchPay.Setup(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()))
                .ThrowsAsync(new NotchPayTransferRejectedException(422, "Numéro invalide"));
            var (service, wallet, db) = Build();

            var result = await service.RequestAsync(Prof, "mtn", "677000001", 2000, "rej");

            Assert.Equal(WithdrawalStatus.Failed, result.Withdrawal.Status);
            Assert.Contains("Numéro invalide", result.Withdrawal.FailureReason);
            Assert.Equal(3000, await wallet.GetAvailableAsync(Prof));
            var (history, _) = await wallet.GetHistoryAsync(Prof, WalletSources.Withdrawal, 1, 20);
            Assert.Equal(2, history.Count); // demande puis contre-passation
            _ntfy.Verify(n => n.PublishAsync(It.IsAny<string>(), "Retrait échoué", It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), Prof, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Once);
            _ntfy.Verify(n => n.PublishAdminAsync("Retrait échoué", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string[]>(),
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task LostResponse_IsReconciledByReference_WithoutSecondTransfer()
        {
            await Fund(Prof, 3000);
            _notchPay.Setup(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()))
                .ThrowsAsync(new HttpRequestException("timeout"));
            var (service, wallet, db) = Build();

            var result = await service.RequestAsync(Prof, "mtn", "677000001", 2000, "lost");
            Assert.Equal(WithdrawalStatus.Pending, result.Withdrawal.Status);
            Assert.Equal(1000, await wallet.GetAvailableAsync(Prof)); // fonds toujours réservés

            ProviderReports("complete");
            await service.SyncAsync(result.Withdrawal.Id);
            await service.SyncByReferenceAsync($"WDR-{result.Withdrawal.Id}"); // notification reçue en plus

            var done = await db.Withdrawals.AsNoTracking().FirstAsync(w => w.Id == result.Withdrawal.Id);
            Assert.Equal(WithdrawalStatus.Completed, done.Status);
            Assert.Equal(1000, await wallet.GetAvailableAsync(Prof));
            Assert.Equal(0, (await wallet.GetBalanceAsync(Prof)).EngagedXaf);
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.WithdrawalProcessed && t.SourceId == done.Id));
            _notchPay.Verify(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()), Times.Once);
        }

        [Fact]
        public async Task TransferNeverCreated_IsFailedAfterGracePeriod_FundsRestored()
        {
            await Fund(Prof, 3000);
            _notchPay.Setup(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>())).ThrowsAsync(new HttpRequestException("down"));
            _notchPay.Setup(n => n.GetTransferAsync(It.IsAny<string>())).ReturnsAsync((NotchPayTransfer?)null);
            var (service, wallet, db) = Build();

            var result = await service.RequestAsync(Prof, "mtn", "677000001", 2000, "never");
            var row = await db.Withdrawals.FirstAsync(w => w.Id == result.Withdrawal.Id);
            row.RequestedAt = DateTime.UtcNow - WithdrawalService.NeverCreatedAfter - TimeSpan.FromMinutes(1);
            await db.SaveChangesAsync();

            await service.SyncAsync(row.Id);

            Assert.Equal(WithdrawalStatus.Failed, (await db.Withdrawals.AsNoTracking().FirstAsync(w => w.Id == row.Id)).Status);
            Assert.Equal(3000, await wallet.GetAvailableAsync(Prof));
        }

        [Fact]
        public async Task FailureNotifiedTwice_RestoresOnlyOnce()
        {
            await Fund(Prof, 3000);
            var (service, wallet, db) = Build();
            var result = await service.RequestAsync(Prof, "mtn", "677000001", 2000, "fail-twice");

            ProviderReports("failed", "Solde marchand insuffisant");
            await service.SyncAsync(result.Withdrawal.Id);
            await service.SyncAsync(result.Withdrawal.Id);

            Assert.Equal(3000, await wallet.GetAvailableAsync(Prof));
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.Reversal && t.SourceId == result.Withdrawal.Id));
            Assert.Equal("Solde marchand insuffisant", (await db.Withdrawals.AsNoTracking().FirstAsync(w => w.Id == result.Withdrawal.Id)).FailureReason);
        }

        // ── Actions de secours (Module 4) ────────────────────────────────

        private async Task<Withdrawal> FailedWithdrawal(WithdrawalService service, string requestId)
        {
            var result = await service.RequestAsync(Prof, "mtn", "677000001", 2000, requestId);
            ProviderReports("failed", "Bénéficiaire refusé");
            return (await service.SyncAsync(result.Withdrawal.Id))!;
        }

        [Fact]
        public async Task Retry_CreatesOneNewTransfer_EvenWhenClickedTwice()
        {
            await Fund(Prof, 3000);
            var (service, wallet, db) = Build();
            var failed = await FailedWithdrawal(service, "to-retry");
            Assert.Equal(WithdrawalStatus.Failed, failed.Status);

            var r1 = await service.RetryAsync(failed.Id, Admin, "Numéro corrigé par le support");
            var r2 = await service.RetryAsync(failed.Id, Admin, "Numéro corrigé par le support");

            Assert.Equal(r1.Id, r2.Id);
            Assert.Equal(failed.Id, r1.RetryOfWithdrawalId);
            Assert.Equal(Admin, r1.ActionByUserId);
            Assert.Equal(1000, await wallet.GetAvailableAsync(Prof)); // nouvelle réservation, une seule
            _notchPay.Verify(n => n.CreateTransferAsync(It.IsAny<NotchPayTransferRequest>()), Times.Exactly(2));
        }

        [Fact]
        public async Task Close_RestoresFundsOnce_AndIsTraced_RescueRefusedOnInFlight()
        {
            await Fund(Prof, 3000);
            var (service, wallet, db) = Build();

            var inFlight = await service.RequestAsync(Prof, "mtn", "677000001", 500, "in-flight");
            ProviderReports("processing");
            Assert.Equal("not_failed", (await Assert.ThrowsAsync<WithdrawalRejectedException>(() =>
                service.CloseAsync(inFlight.Withdrawal.Id, Admin, "test"))).Code);

            var failed = await FailedWithdrawal(service, "to-close");
            var closed = await service.CloseAsync(failed.Id, Admin, "Bénéficiaire injoignable");

            Assert.NotNull(closed.ClosedAt);
            Assert.Equal(Admin, closed.ClosedByUserId);
            Assert.Equal("Bénéficiaire injoignable", closed.AdminNote);
            Assert.Equal(2500, await wallet.GetAvailableAsync(Prof)); // 3000 - 500 encore en cours
            Assert.Equal(1, await db.WalletTransactions.CountAsync(t => t.EntryType == WalletEntryTypes.Reversal && t.SourceId == failed.Id));
        }

        [Fact]
        public async Task CompletedAfterRestitution_IsFlaggedAsAnomaly_NotSilentlyCorrected()
        {
            await Fund(Prof, 3000);
            var (service, wallet, db) = Build();
            var failed = await FailedWithdrawal(service, "anomaly");

            ProviderReports("complete");
            await service.SyncAsync(failed.Id);

            var row = await db.Withdrawals.AsNoTracking().FirstAsync(w => w.Id == failed.Id);
            Assert.Equal(WithdrawalStatus.Failed, row.Status);
            Assert.NotNull(row.Anomaly);
            Assert.Equal(3000, await wallet.GetAvailableAsync(Prof));
            await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.RetryAsync(failed.Id, Admin, "rejeu"));
        }

        [Fact]
        public async Task ManualValidation_IsRefusedForAutomatedWithdrawals()
        {
            await Fund(Prof, 3000);
            var (service, _, _) = Build();
            var result = await service.RequestAsync(Prof, "mtn", "677000001", 1000, "auto");

            var ex = await Assert.ThrowsAsync<WithdrawalRejectedException>(() => service.SettleLegacyManualAsync(result.Withdrawal.Id, Admin, true, null));
            Assert.Equal("automated", ex.Code);
        }
    }
}
