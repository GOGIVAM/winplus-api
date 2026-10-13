using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Middlewares;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7a (suivi 18.1 à 18.9) : garde-fous des comptes, journal d'audit
    /// écrit par les actions sensibles, réglages lus et appliqués (InMemory).
    /// </summary>
    public class AdminLot97aTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _dbName = $"admin97a-{Guid.NewGuid()}";
        private const int AdminA = 9701, AdminB = 9702, Student = 9703, Teacher = 9704;

        public AdminLot97aTests()
        {
            using var db = Fresh();
            db.Users.AddRange(
                new User { Id = AdminA, Email = "admin.a@test.local", Role = "admin", IsActive = true },
                new User { Id = AdminB, Email = "admin.b@test.local", Role = "admin", IsActive = true },
                new User { Id = Student, Email = "eleve@test.local", FirstName = "Awa", Role = "student", IsActive = true },
                new User { Id = Teacher, Email = "prof@test.local", Role = "teacher", IsActive = true });
            db.UserSessions.Add(new UserSession { UserId = Student, IsActive = true, LastActivityAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(1) });
            db.SaveChanges();
        }

        private TestApplicationDbContext Fresh() => TestApplicationDbContext.Create(_dbName, Root);

        private static ClaimsPrincipal Principal(int id, string role, string email) =>
            new(new ClaimsIdentity(new[]
            {
                new Claim("user_id", id.ToString()), new Claim("sub", id.ToString()),
                new Claim("role", role), new Claim(ClaimTypes.Role, role), new Claim("email", email),
            }, "test"));

        private AdminUsersController Users(ApplicationDbContext db, int actor = AdminA) =>
            new(db, new Mock<IEmailService>().Object, new MemoryCache(new MemoryCacheOptions()),
                NullLogger<AdminUsersController>.Instance, new AdminAuditService(db, NullLogger<AdminAuditService>.Instance))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext { User = Principal(actor, "admin", actor == AdminA ? "admin.a@test.local" : "admin.b@test.local") },
                },
            };

        // ── PUT /admin/users/{id} : plus de rôle ni de statut ─────────────

        [Fact]
        public async Task PutUser_RefusesRoleAndStatusChanges_ButUpdatesNameWithAudit()
        {
            using var db = Fresh();
            var c = Users(db);

            var role = await c.Update(Student, new Backend.Controllers.AdminUpdateUserRequest { Role = "admin" });
            Assert.IsType<BadRequestObjectResult>(role);
            var status = await c.Update(Student, new Backend.Controllers.AdminUpdateUserRequest { Status = "suspended" });
            Assert.IsType<BadRequestObjectResult>(status);

            var ok = await c.Update(Student, new Backend.Controllers.AdminUpdateUserRequest { FirstName = "Awa-Marie", Role = "student", Status = "active" });
            Assert.IsType<OkObjectResult>(ok);

            using var check = Fresh();
            var u = await check.Users.FindAsync(Student);
            Assert.Equal("student", u!.Role);
            Assert.True(u.IsActive);
            Assert.Equal("Awa-Marie", u.FirstName);
            var audit = Assert.Single(check.AdminAuditLogs.Where(a => a.TargetId == Student.ToString()));
            Assert.Equal(AdminAuditActions.UserUpdated, audit.Action);
            Assert.Equal(AdminA, audit.ActorUserId);
        }

        [Fact]
        public async Task SetRole_RequiresReason_RevokesSessions_AndWritesAudit()
        {
            using var db = Fresh();
            var c = Users(db);

            Assert.IsType<BadRequestObjectResult>(await c.SetRole(Student, new AdminSetRoleRequest { Role = "teacher" }));

            var ok = await c.SetRole(Student, new AdminSetRoleRequest { Role = "teacher", Reason = "Bascule demandée par l'établissement" });
            Assert.IsType<OkObjectResult>(ok);

            using var check = Fresh();
            Assert.Equal("teacher", (await check.Users.FindAsync(Student))!.Role);
            Assert.False(check.UserSessions.Single(s => s.UserId == Student).IsActive);
            var audit = check.AdminAuditLogs.Single(a => a.Action == AdminAuditActions.UserRoleChanged);
            Assert.Contains("student", audit.Before);
            Assert.Contains("teacher", audit.After);
            Assert.Equal("Bascule demandée par l'établissement", audit.Reason);
        }

        [Fact]
        public async Task LastActiveAdmin_CannotBeDemotedOrSuspended()
        {
            using var db = Fresh();
            // AdminB suspend AdminA : autorisé (il reste AdminB).
            Assert.IsType<OkObjectResult>(await Users(db, AdminB).Suspend(AdminA, new AdminSuspendRequest { Reason = "Compte compromis, enquête" }));

            using var db2 = Fresh();
            // AdminA (suspendu) mis à part, AdminB est le dernier administrateur actif.
            var result = await Users(db2, AdminA).SetRole(AdminB, new AdminSetRoleRequest { Role = "teacher", Reason = "Changement de poste" });
            var conflict = Assert.IsType<ConflictObjectResult>(result);
            Assert.Contains("last_admin", JsonSerializer.Serialize(conflict.Value));
        }

        [Fact]
        public async Task Suspend_WithoutReason_IsRefused_WithReason_IsAudited()
        {
            using var db = Fresh();
            Assert.IsType<BadRequestObjectResult>(await Users(db).Suspend(Teacher, new AdminSuspendRequest()));
            Assert.IsType<OkObjectResult>(await Users(db).Suspend(Teacher, new AdminSuspendRequest { Reason = "Contenus frauduleux" }));
            Assert.IsType<OkObjectResult>(await Users(db).Reactivate(Teacher, new AdminReasonRequest { Reason = "Après vérification" }));

            using var check = Fresh();
            var actions = check.AdminAuditLogs.Where(a => a.TargetId == Teacher.ToString()).Select(a => a.Action).ToList();
            Assert.Contains(AdminAuditActions.UserSuspended, actions);
            Assert.Contains(AdminAuditActions.UserReactivated, actions);
        }

        // ── Réglages : lus et appliqués ───────────────────────────────────

        [Fact]
        public void Settings_OverrideConfiguration_AndAreApplied()
        {
            var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TutorBooking:DisputeWindowHours"] = "24",
                ["TutorSafety:ReportThreshold"] = "3",
            }).Build();

            // Sans valeur en base : la configuration historique s'applique.
            var empty = PlatformSettings.FromValues(new Dictionary<string, string>(), config);
            Assert.Equal(24, TutorPolicySettings.From(config, empty).DisputeWindowHours);

            var settings = PlatformSettings.FromValues(new Dictionary<string, string>
            {
                [PlatformSettingDefinitions.TutorDisputeWindowHours] = "48",
                [PlatformSettingDefinitions.NotchPayFeePercent] = "2.5",
                [PlatformSettingDefinitions.TutorReportThreshold] = "5",
                [PlatformSettingDefinitions.WinAiChatTemperature] = "0.2",
            }, config);

            var policy = TutorPolicySettings.From(config, settings);
            Assert.Equal(48, policy.DisputeWindowHours);
            Assert.Equal(2.5m, policy.NotchPayFeePercent);
            Assert.Equal(5, TutorVerificationSettings.From(config, settings).ReportThreshold);
            Assert.Equal(0.2m, settings.GetDecimal(PlatformSettingDefinitions.WinAiChatTemperature));
            // Valeur par défaut d'un réglage jamais modifié.
            Assert.True(settings.GetBool(PlatformSettingDefinitions.SignupEnabled));
        }

        [Fact]
        public void Settings_Normalize_RejectsOutOfBounds()
        {
            var def = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.WinAiChatMaxTokens)!;
            Assert.NotNull(PlatformSettingDefinitions.Normalize(def, "50").Error);
            Assert.Equal("4000", PlatformSettingDefinitions.Normalize(def, "4000").Value);
            var flag = PlatformSettingDefinitions.Find(PlatformSettingDefinitions.MaintenanceMode)!;
            Assert.NotNull(PlatformSettingDefinitions.Normalize(flag, "peut-être").Error);
        }

        [Fact]
        public async Task SettingUpdate_IsPersisted_Applied_AndAudited()
        {
            using var db = Fresh();
            var settings = PlatformSettings.FromContext(() => Fresh());
            var admin = new PlatformSettingsAdminService(db, settings, new AdminAuditService(db, NullLogger<AdminAuditService>.Instance));

            var (ok, error) = await admin.UpdateAsync(PlatformSettingDefinitions.PromoCodesEnabled, "false",
                Principal(AdminA, "admin", "admin.a@test.local"), "127.0.0.1");
            Assert.True(ok, error);
            Assert.False(settings.GetBool(PlatformSettingDefinitions.PromoCodesEnabled));

            // Appliqué : la validation d'un code est refusée.
            var promo = new PromoCodeService(db, NullLogger<PromoCodeService>.Instance, settings);
            var result = await promo.ValidatePromoCodeAsync(Student, new ValidatePromoCodeRequest { Code = "RENTREE", CartTotal = 1000 });
            Assert.False(result.IsValid);
            Assert.Contains("désactivés", result.ErrorMessage);

            using var check = Fresh();
            var audit = check.AdminAuditLogs.Single(a => a.Action == AdminAuditActions.SettingChanged);
            Assert.Equal(PlatformSettingDefinitions.PromoCodesEnabled, audit.TargetId);
            Assert.Contains("true", audit.Before);
            Assert.Contains("false", audit.After);
        }

        [Fact]
        public async Task SubscriptionOnlyPromoCode_IsRefusedOnCatalogOrders()
        {
            using var db = Fresh();
            db.PromoCodes.Add(new PromoCode
            {
                Code = "ABO10", DiscountType = "Percentage", DiscountValue = 10, ValidFrom = DateTime.UtcNow.AddDays(-1),
                IsActive = true, CreatedBy = AdminA, AppliesTo = PromoCodeScopes.Subscription,
            });
            await db.SaveChangesAsync();
            var promo = new PromoCodeService(db, NullLogger<PromoCodeService>.Instance, PlatformSettings.FromValues(new Dictionary<string, string>()));
            var result = await promo.ValidatePromoCodeAsync(Student, new ValidatePromoCodeRequest { Code = "abo10", CartTotal = 1000 });
            Assert.False(result.IsValid);
        }

        [Fact]
        public async Task Maintenance_BlocksNonAdmins_LetsAdminsAndAuthThrough()
        {
            var settings = PlatformSettings.FromValues(new Dictionary<string, string>
            {
                [PlatformSettingDefinitions.MaintenanceMode] = "true",
                [PlatformSettingDefinitions.MaintenanceMessage] = "Retour à 14 h",
            });
            var reached = 0;
            var mw = new MaintenanceMiddleware(_ => { reached++; return Task.CompletedTask; });

            async Task<int> Run(string path, ClaimsPrincipal? user)
            {
                var ctx = new DefaultHttpContext();
                ctx.Request.Path = path;
                ctx.Response.Body = new System.IO.MemoryStream();
                if (user != null) ctx.User = user;
                await mw.InvokeAsync(ctx, settings);
                return ctx.Response.StatusCode;
            }

            Assert.Equal(503, await Run("/api/subjects", Principal(Student, "student", "eleve@test.local")));
            Assert.Equal(0, reached);
            Assert.Equal(200, await Run("/api/subjects", Principal(AdminA, "admin", "admin.a@test.local")));
            Assert.Equal(200, await Run("/api/auth/signin", null));
            Assert.Equal(200, await Run("/api/payments/webhook/notchpay", null));
            Assert.Equal(3, reached);
        }

        [Fact]
        public void Announcements_TargetedRolesOnly()
        {
            var forParents = new Announcement { Title = "x", TargetRoles = "parent" };
            Assert.True(AnnouncementController.IsVisibleTo(forParents, "parent"));
            Assert.False(AnnouncementController.IsVisibleTo(forParents, "student"));
            Assert.False(AnnouncementController.IsVisibleTo(forParents, null));
            Assert.True(AnnouncementController.IsVisibleTo(new Announcement { Title = "x" }, null));
        }

        [Fact]
        public void DiplomaUrl_MustBeHttpsOnPlatformStorage()
        {
            Assert.False(TutorProfileService.IsAllowedDocumentUrl("javascript:alert(1)", "bucket.s3.amazonaws.com"));
            Assert.False(TutorProfileService.IsAllowedDocumentUrl("http://bucket.s3.amazonaws.com/d.pdf", "bucket.s3.amazonaws.com"));
            Assert.False(TutorProfileService.IsAllowedDocumentUrl("https://evil.example/d.pdf", "bucket.s3.amazonaws.com"));
            Assert.True(TutorProfileService.IsAllowedDocumentUrl("https://bucket.s3.amazonaws.com/d.pdf", "bucket.s3.amazonaws.com"));
        }
    }

    /// <summary>
    /// Lot 9.7a sur un vrai PostgreSQL (WINPLUS_TEST_PG) : conversion calculée
    /// en SQL et remboursement catalogue crédité une seule fois (18.5).
    /// </summary>
    public class AdminLot97aPostgresTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;

        public AdminLot97aPostgresTests(PostgresTestDatabase pg) => _pg = pg;

        [PostgresFact]
        public async Task Conversion_IsComputedInSql_OverThePeriodCohort()
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            var now = DateTime.UtcNow;
            // Fenêtre isolée dans le passé pour ne pas croiser les autres tests de la base partagée.
            var from = now.AddYears(-30);
            var to = from.AddDays(30);
            await using (var db = _pg.CreateContext())
            {
                var buyer = new User { Email = $"buyer-{tag}@it.local", Role = "student", IsActive = true, CreatedAt = from.AddDays(1) };
                var lurker = new User { Email = $"lurker-{tag}@it.local", Role = "student", IsActive = true, CreatedAt = from.AddDays(2) };
                var late = new User { Email = $"late-{tag}@it.local", Role = "student", IsActive = true, CreatedAt = from.AddDays(3) };
                var outside = new User { Email = $"old-{tag}@it.local", Role = "student", IsActive = true, CreatedAt = from.AddDays(-5) };
                db.Users.AddRange(buyer, lurker, late, outside);
                await db.SaveChangesAsync();
                db.Orders.AddRange(
                    new Order { OrderNumber = $"C1-{tag}", UserId = buyer.Id, Status = "completed", TotalAmount = 1000, CreatedAt = from.AddDays(4) },
                    new Order { OrderNumber = $"C2-{tag}", UserId = buyer.Id, Status = "completed", TotalAmount = 1000, CreatedAt = from.AddDays(5) },
                    new Order { OrderNumber = $"C3-{tag}", UserId = late.Id, Status = "pending", TotalAmount = 1000, CreatedAt = from.AddDays(6) },
                    new Order { OrderNumber = $"C4-{tag}", UserId = outside.Id, Status = "completed", TotalAmount = 1000, CreatedAt = from.AddDays(6) });
                await db.SaveChangesAsync();
            }

            await using var read = _pg.CreateContext();
            var (registered, buyers, rate) = await AdminMetrics.ConversionAsync(read, from, to);
            Assert.Equal(3, registered);   // inscrits sur la période (pas « outside »)
            Assert.Equal(1, buyers);       // une seule personne a une commande encaissée, comptée une fois
            Assert.Equal(33.33, rate);
        }

        [PostgresFact]
        public async Task ApprovedCatalogRefund_CreditsBuyerOnce_WithFeeRule()
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            int buyerId, adminId, orderId;
            await using (var db = _pg.CreateContext())
            {
                var author = new User { Email = $"auteur-{tag}@it.local", Role = "teacher", IsActive = true };
                var buyer = new User { Email = $"acheteur-{tag}@it.local", Role = "parent", IsActive = true };
                var admin = new User { Email = $"admin-{tag}@it.local", Role = "admin", IsActive = true };
                db.Users.AddRange(author, buyer, admin);
                await db.SaveChangesAsync();
                var subject = new Subject { Title = $"Épreuve {tag}", Price = 5000, IsPublished = true, AuthorUserId = author.Id, PlatformCommissionRate = 0.2m };
                db.Subjects.Add(subject);
                await db.SaveChangesAsync();
                var order = new Order { OrderNumber = $"R-{tag}", UserId = buyer.Id, Status = "completed", TotalAmount = 5000, PaymentMethod = "mobile_money", CompletedDate = DateTime.UtcNow };
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                db.OrderItems.Add(new OrderItem { OrderId = order.Id, SubjectId = subject.Id, PriceAtPurchase = 5000 });
                db.Payments.Add(new Payment { OrderId = order.Id, UserId = buyer.Id, Amount = 5000, Status = "completed", PaymentMethod = "notchpay", CompletedAt = DateTime.UtcNow });
                await db.SaveChangesAsync();
                // Vente enregistrée comme en production, puis demande de remboursement.
                await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(order.Id);
                order.Status = "refund_requested";
                await db.SaveChangesAsync();
                buyerId = buyer.Id; adminId = admin.Id; orderId = order.Id;
            }

            async Task<IActionResult> Approve()
            {
                await using var db = _pg.CreateContext();
                var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
                var controller = new AdminRefundsController(db, new Mock<INtfyService>().Object, new Mock<IContentAccessService>().Object,
                    NullLogger<AdminRefundsController>.Instance, wallet, new CatalogRefundService(db),
                    new AdminAuditService(db, NullLogger<AdminAuditService>.Instance))
                {
                    ControllerContext = new ControllerContext
                    {
                        HttpContext = new DefaultHttpContext
                        {
                            User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", adminId.ToString()), new Claim("role", "admin") }, "test")),
                        },
                    },
                };
                return await controller.Approve(orderId, new AdminRefundDecisionRequest("Contenu illisible"));
            }

            Assert.IsType<OkObjectResult>(await Approve());
            // Double clic / deuxième administrateur : refusé, rien de plus n'est crédité.
            Assert.IsType<ConflictObjectResult>(await Approve());
            // Réconciliation rejouée : idempotente.
            await using (var db = _pg.CreateContext())
                await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(orderId);

            await using var check = _pg.CreateContext();
            var credits = await check.WalletTransactions.Where(t => t.OwnerId == buyerId && t.EntryType == WalletEntryTypes.CatalogRefund).ToListAsync();
            var credit = Assert.Single(credits);
            Assert.Equal(4850m, credit.Amount);   // 5 000 − frais NotchPay 3 % (150), règle du 9.2
            var fee = Assert.Single(await check.WalletTransactions.Where(t => t.SourceType == "Order" && t.SourceId == orderId
                && t.EntryType == WalletEntryTypes.PaymentFeeRecovered).ToListAsync());
            Assert.Equal(150m, fee.Amount);
            // Vente et commission du vendeur contre-passées : la somme des écritures de la commande = encaissé.
            var orderItemIds = await check.OrderItems.Where(oi => oi.OrderId == orderId).Select(oi => oi.Id).ToListAsync();
            var saleSum = await check.WalletTransactions
                .Where(t => t.SourceType == "OrderItem" && t.SourceId != null && orderItemIds.Contains(t.SourceId.Value))
                .SumAsync(t => t.Amount);
            Assert.Equal(0m, saleSum);
            Assert.Equal(1, await check.AdminAuditLogs.CountAsync(a => a.Action == AdminAuditActions.RefundApproved && a.TargetId == orderId.ToString()));
        }
    }
}
