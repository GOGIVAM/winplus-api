using System;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 2, Module 3 : recharge, paiement combiné solde + Mobile Money,
    /// crédit manuel administrateur. Vrai WalletService et vrai contrôleur
    /// administrateur sur le modèle EF (InMemory).
    /// </summary>
    public class WalletModule3Tests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"wallet3-{Guid.NewGuid()}";

        private const int Prof = 9301;
        private const int Author = 9302;
        private const int Parent = 9303;
        private const int Student = 9304;
        private const int SuspendedProf = 9305;
        private const int Admin = 9399;

        public WalletModule3Tests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = Prof, Email = "p3@test.local", Role = "teacher" },
                new User { Id = Author, Email = "a3@test.local", Role = "teacher" },
                new User { Id = Parent, Email = "par3@test.local", Role = "parent" },
                new User { Id = Student, Email = "s3@test.local", Role = "student" },
                new User { Id = SuspendedProf, Email = "sp3@test.local", Role = "teacher", IsActive = false },
                new User { Id = Admin, Email = "adm3@test.local", Role = "admin" });
            db.Subjects.Add(new Subject { Id = 531, Title = "Physique Tle", Price = 3000, AuthorUserId = Author });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);
        private static WalletService Wallet(ApplicationDbContext db) => new(db, NullLogger<WalletService>.Instance);

        // ── Recharge ─────────────────────────────────────────────────────

        [Fact]
        public async Task ConfirmedRecharge_CreditsExactAmountOnce_AsRechargeNotEarning()
        {
            using var db = Fresh();
            db.Orders.Add(new Order { Id = 7301, UserId = Prof, OrderNumber = "WP-RCH-1", TotalAmount = 5000, Status = "pending", PaymentMethod = "mtn" });
            db.WalletTopUps.Add(new WalletTopUp { Id = 1, UserId = Prof, OrderId = 7301, AmountXaf = 5000 });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);

            await wallet.SyncOrderAsync(7301); // paiement pas encore confirmé
            Assert.Equal(0, await wallet.GetAvailableAsync(Prof));

            (await db.Orders.FirstAsync(o => o.Id == 7301)).Status = "completed";
            await db.SaveChangesAsync();
            await wallet.SyncOrderAsync(7301);
            await wallet.SyncOrderAsync(7301); // notification rejouée

            Assert.Equal(5000, await wallet.GetAvailableAsync(Prof));
            var (recharges, total) = await wallet.GetHistoryAsync(Prof, WalletSources.Recharge, 1, 10);
            Assert.Equal(1, total);
            Assert.Equal(WalletSources.Recharge, recharges[0].Source);
            Assert.Equal("completed", (await db.WalletTopUps.AsNoTracking().FirstAsync(t => t.Id == 1)).Status);
        }

        [Fact]
        public async Task FailedRecharge_WritesNothing()
        {
            using var db = Fresh();
            db.Orders.Add(new Order { Id = 7302, UserId = Prof, OrderNumber = "WP-RCH-2", TotalAmount = 5000, Status = "failed", PaymentMethod = "mtn" });
            db.WalletTopUps.Add(new WalletTopUp { Id = 2, UserId = Prof, OrderId = 7302, AmountXaf = 5000 });
            await db.SaveChangesAsync();
            var wallet = Wallet(db);

            await wallet.SyncOrderAsync(7302);

            Assert.Equal(0, await wallet.GetAvailableAsync(Prof));
            Assert.Equal("failed", (await db.WalletTopUps.AsNoTracking().FirstAsync(t => t.Id == 2)).Status);
        }

        // ── Paiement combiné solde + Mobile Money ────────────────────────

        private async Task<WalletService> SplitOrder(ApplicationDbContext db, int orderId, string status)
        {
            var wallet = Wallet(db);
            await wallet.PostAsync(new WalletEntry(Prof, WalletEntryTypes.AdminCredit, 1000, $"seed-{orderId}", "Crédit"));
            db.Orders.Add(new Order { Id = orderId, UserId = Prof, OrderNumber = $"WP-{orderId}", TotalAmount = 3000, Status = "pending", PaymentMethod = "mtn" });
            db.OrderItems.Add(new OrderItem { Id = orderId, OrderId = orderId, SubjectId = 531, PriceAtPurchase = 3000 });
            await db.SaveChangesAsync();
            await wallet.RunLockedAsync(Prof, () => wallet.DebitAsync(new WalletEntry(Prof, WalletEntryTypes.BalancePurchase, 1000,
                WalletService.BalancePurchaseKey(orderId), "Achat (part solde)", "Order", orderId)));

            await wallet.SyncOrderAsync(orderId); // complément encore en attente : rien à restituer
            Assert.Equal(0, await wallet.GetAvailableAsync(Prof));

            (await db.Orders.FirstAsync(o => o.Id == orderId)).Status = status;
            await db.SaveChangesAsync();
            await wallet.SyncOrderAsync(orderId);
            return wallet;
        }

        [Fact]
        public async Task CombinedPayment_ComplementFails_BalancePartIsRestored()
        {
            using var db = Fresh();
            var wallet = await SplitOrder(db, 7311, "failed");

            Assert.Equal(1000, await wallet.GetAvailableAsync(Prof));
            Assert.Equal(0, await wallet.GetAvailableAsync(Author));
        }

        [Fact]
        public async Task CombinedPayment_ComplementPaid_BalanceKept_AuthorCredited()
        {
            using var db = Fresh();
            var wallet = await SplitOrder(db, 7312, "completed");

            Assert.Equal(0, await wallet.GetAvailableAsync(Prof));
            Assert.Equal(3000, await wallet.GetAvailableAsync(Author));
        }

        // ── Crédit manuel administrateur ─────────────────────────────────

        private AdminWalletController AdminController(ApplicationDbContext db, Mock<INtfyService>? ntfy = null)
        {
            var wallet = Wallet(db);
            var controller = new AdminWalletController(
                new WalletBackfillService(db, wallet, NullLogger<WalletBackfillService>.Instance),
                wallet, NullLogger<AdminWalletController>.Instance, db, (ntfy ?? new Mock<INtfyService>()).Object);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("user_id", Admin.ToString()),
                        new Claim(ClaimTypes.Role, "admin"),
                    }, "test")),
                },
            };
            return controller;
        }

        private static int StatusOf(IActionResult result) => result switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => 0,
        };

        [Fact]
        public async Task AdminCredit_DoubleSubmit_CreditsOnce_AttributedToAdminWithReason()
        {
            using var db = Fresh();
            var ntfy = new Mock<INtfyService>();
            var controller = AdminController(db, ntfy);
            var request = new AdminWalletController.AdminCreditRequest(Prof, 2500, "Geste commercial : panne du 02/10", "form-1");

            Assert.Equal(200, StatusOf(await controller.Credit(request)));
            Assert.Equal(200, StatusOf(await controller.Credit(request)));

            var entry = await db.WalletTransactions.AsNoTracking().SingleAsync(t => t.OwnerId == Prof && t.EntryType == WalletEntryTypes.AdminCredit);
            Assert.Equal(2500, entry.Amount);
            Assert.Equal(Admin, entry.CreatedByUserId);
            Assert.Contains("Geste commercial", entry.Description);
            Assert.Equal(2500, await Wallet(db).GetAvailableAsync(Prof));
            ntfy.Verify(n => n.PublishAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(),
                It.IsAny<string[]>(), Prof, It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int?>()), Times.Once);
        }

        [Fact]
        public async Task AdminCredit_ParentAllowed_OthersAndInvalidRequestsRefused()
        {
            using var db = Fresh();
            var controller = AdminController(db);

            Assert.Equal(200, StatusOf(await controller.Credit(new(Parent, 1000, "Correction d'erreur", "k-parent"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(Student, 1000, "Motif valide", "k-student"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(SuspendedProf, 1000, "Motif valide", "k-susp"))));
            Assert.Equal(404, StatusOf(await controller.Credit(new(999999, 1000, "Motif valide", "k-none"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(Prof, 0, "Motif valide", "k-zero"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(Prof, -500, "Motif valide", "k-neg"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(Prof, 100.5m, "Motif valide", "k-frac"))));
            Assert.Equal(400, StatusOf(await controller.Credit(new(Prof, 1000, "  ", "k-noreason"))));
            // Même clé réutilisée pour un autre montant : refus explicite, pas de second crédit.
            Assert.Equal(409, StatusOf(await controller.Credit(new(Parent, 2000, "Correction d'erreur", "k-parent"))));

            Assert.Equal(1000, await Wallet(db).GetAvailableAsync(Parent));
            Assert.Equal(0, await Wallet(db).GetAvailableAsync(Prof));
        }

        [Fact]
        public async Task Treasury_PlatformShareAndDueAmountsReconcileWithLedger()
        {
            using var db = Fresh();
            var wallet = Wallet(db);
            db.TutorProfiles.Add(new TutorProfile { Id = 631, UserId = Prof });
            db.TutorBookings.Add(new TutorBooking { Id = 632, TutorProfileId = 631, StudentUserId = Student, PriceXaf = 10000,
                Status = "completed", PaymentStatus = "completed", EscrowReleasedAt = DateTime.UtcNow, SessionDate = new DateOnly(2026, 9, 30) });
            await db.SaveChangesAsync();
            await wallet.SyncTutorBookingAsync(632);

            var result = Assert.IsType<OkObjectResult>(await AdminController(db).Treasury());
            using var doc = JsonDocument.Parse(JsonSerializer.Serialize(result.Value));
            var data = doc.RootElement.GetProperty("data");

            // 10 000 encaissés = 8 000 dus au professeur + 2 000 de part plateforme.
            Assert.Equal(10000, data.GetProperty("collectedTutoringXaf").GetDecimal());
            Assert.Equal(2000, data.GetProperty("platformCommissionXaf").GetDecimal());
            Assert.True(data.GetProperty("dueAvailableXaf").GetDecimal() >= 8000);
        }
    }
}
