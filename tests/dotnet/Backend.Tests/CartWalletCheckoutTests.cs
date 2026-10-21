using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Paiement du panier par le portefeuille dans la fenêtre d'achat : le
    /// parent pour un enfant lié (ParentCartCheckoutController), l'élève et le
    /// professeur pour eux-mêmes (OrdersController.PayWithBalance et
    /// pay-with-balance-complement). Moyen toujours au choix : portefeuille
    /// seul, portefeuille + Mobile Money (restitution si le complément
    /// échoue), Mobile Money seul.
    ///
    /// Sur InMemory : pas de transaction réelle, le verrou du portefeuille est
    /// un verrou de processus. La sérialisation PostgreSQL est couverte par les
    /// tests [PostgresFact] existants du paiement par solde.
    /// </summary>
    public class CartWalletCheckoutTests
    {
        private sealed record World(int Author, int Parent, int Child, int OtherChild, int Student, int Teacher,
            int SubjectA, int SubjectB, int Course);

        private static async Task<World> SeedAsync(ApplicationDbContext db, string parentLocale = "fr")
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            var author = new User { Email = $"auteur-{tag}@t.local", Role = "teacher", IsActive = true, FirstName = "Auteur", LastName = "T" };
            var parent = new User { Email = $"parent-{tag}@t.local", Role = "parent", IsActive = true, FirstName = "Paul", LastName = "P", Locale = parentLocale };
            var child = new User { Email = $"enfant-{tag}@t.local", Role = "student", IsActive = true, FirstName = "Ana", LastName = "P" };
            var other = new User { Email = $"autre-{tag}@t.local", Role = "student", IsActive = true, FirstName = "Zoe", LastName = "X" };
            var student = new User { Email = $"eleve-{tag}@t.local", Role = "student", IsActive = true, FirstName = "Eli", LastName = "E" };
            var teacher = new User { Email = $"prof-{tag}@t.local", Role = "teacher", IsActive = true, FirstName = "Pro", LastName = "F" };
            db.Users.AddRange(author, parent, child, other, student, teacher);
            await db.SaveChangesAsync();
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted", InitiatedBy = parent.Id });
            // Lien en attente : l'enfant n'a pas accepté, il ne compte pas.
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = other.Id, Status = "pending", InitiatedBy = parent.Id });
            var a = new Subject { Title = $"BAC A {tag}", Price = 1193m, IsPublished = true, AuthorUserId = author.Id, PlatformCommissionRate = 0.2m };
            var b = new Subject { Title = $"BAC B {tag}", Price = 2386m, IsPublished = true, AuthorUserId = author.Id, PlatformCommissionRate = 0.2m };
            db.Subjects.AddRange(a, b);
            var course = new Course
            {
                Title = $"Formation {tag}", Slug = $"f-{tag}", InstructorId = author.Id, Status = "published",
                Price = 5963m, IsFree = false, PlatformCommissionRate = 0.2m,
            };
            db.Courses.Add(course);
            await db.SaveChangesAsync();
            return new World(author.Id, parent.Id, child.Id, other.Id, student.Id, teacher.Id, a.Id, b.Id, course.Id);
        }

        private static async Task CreditAsync(ApplicationDbContext db, int owner, decimal amount) =>
            await new WalletService(db, NullLogger<WalletService>.Instance)
                .PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"seed-{owner}-{Guid.NewGuid()}", "Recharge de test"));

        private static Task<decimal> AvailableAsync(ApplicationDbContext db, int owner) =>
            new WalletService(db, NullLogger<WalletService>.Instance).GetAvailableAsync(owner);

        private static async Task FillCartAsync(ApplicationDbContext db, int userId, params (int? SubjectId, int? CourseId, decimal Price)[] lines)
        {
            foreach (var l in lines)
                db.CartItems.Add(new CartItem { UserId = userId, SubjectId = l.SubjectId, CourseId = l.CourseId, Price = l.Price });
            await db.SaveChangesAsync();
        }

        private static Mock<IPaymentService> Payments(bool fail = false)
        {
            var mock = new Mock<IPaymentService>();
            var setup = mock.Setup(p => p.InitiateNotchPayAsync(It.IsAny<int?>(), It.IsAny<InitiatePaymentRequest>()));
            if (fail) setup.ThrowsAsync(new HttpRequestException("NotchPay injoignable"));
            else setup.ReturnsAsync((int? _, InitiatePaymentRequest r) => new InitiatePaymentResponse { PaymentId = 4242, Status = "pending", Amount = r.Amount });
            return mock;
        }

        private static ParentCartCheckoutController ParentController(ApplicationDbContext db, int parentId, Mock<IPaymentService>? payments = null)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var controller = new ParentCartCheckoutController(db, wallet, (payments ?? Payments()).Object,
                new PromoCodeService(db, NullLogger<PromoCodeService>.Instance), new Mock<INtfyService>().Object,
                new Backend.Services.Entitlements.EntitlementService(db, new MemoryCache(new MemoryCacheOptions())),
                NullLogger<ParentCartCheckoutController>.Instance);
            return CourseSales97bTests.Controller(controller, parentId, "parent");
        }

        private static OrdersController Orders(ApplicationDbContext db, int userId, string role, Mock<IPaymentService>? payments = null)
        {
            var orderService = new OrderService(
                new OrderRepository(db, NullLogger<OrderRepository>.Instance),
                new CartRepository(db, NullLogger<CartRepository>.Instance),
                new SubjectRepository(db, NullLogger<SubjectRepository>.Instance),
                new PromoCodeService(db, NullLogger<PromoCodeService>.Instance),
                db, NullLogger<OrderService>.Instance);
            var controller = new OrdersController(orderService, NullLogger<OrdersController>.Instance, db,
                new Mock<IPdfService>().Object, new Mock<ITeacherService>().Object, new Mock<IAffiliateService>().Object,
                new WalletService(db, NullLogger<WalletService>.Instance), (payments ?? Payments()).Object,
                promoCodes: new PromoCodeService(db, NullLogger<PromoCodeService>.Instance));
            return CourseSales97bTests.Controller(controller, userId, role);
        }

        private static JsonElement Body(IActionResult result)
        {
            var value = result switch
            {
                ObjectResult o => o.Value,
                _ => throw new InvalidOperationException(result.GetType().Name),
            };
            return JsonDocument.Parse(JsonSerializer.Serialize(value)).RootElement;
        }

        private static int Status(IActionResult result) => (result as ObjectResult)?.StatusCode ?? 200;

        private static string? Code(IActionResult result) =>
            Body(result).TryGetProperty("code", out var c) ? c.GetString() : null;

        // ── Parent : portefeuille seul ───────────────────────────────────

        [Fact]
        public async Task Parent_SufficientBalance_MixedCart_PaysOnceForTheChild()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 10_000m);
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m), (null, w.Course, 5963m));

            var result = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });

            Assert.Equal(200, Status(result));
            var order = await db.Orders.Include(o => o.Items).SingleAsync(o => o.UserId == w.Parent);
            Assert.Equal("completed", order.Status);
            Assert.Equal("parent_wallet", order.PaymentMethod);
            Assert.Equal(CourseSales.ChildMarkerPrefix + w.Child, order.Notes);
            Assert.Equal(1193m + 5963m, order.TotalAmount);
            Assert.Equal(2, order.Items.Count);
            Assert.Equal(10_000m - 7156m, await AvailableAsync(db, w.Parent));
            Assert.Empty(db.CartItems.Where(c => c.UserId == w.Parent));

            // Droits de l'enfant : épreuve (inscription + commande d'un parent lié) et formation.
            Assert.True(await db.Enrollments.AnyAsync(e => e.UserId == w.Child && e.SubjectId == w.SubjectA));
            var ce = await db.CourseEnrollments.SingleAsync(e => e.CourseId == w.Course);
            Assert.Equal(w.Child, ce.UserId);
            Assert.True(ce.IsActive);
            var access = new ContentAccessService(db, new Backend.Services.Entitlements.EntitlementService(db, new MemoryCache(new MemoryCacheOptions())));
            Assert.True(await access.HasPaidContentAccessAsync(w.Child, await db.Subjects.SingleAsync(s => s.Id == w.SubjectA), false));
            // Le parent, lui, ne reçoit rien en propre.
            Assert.False(await db.Enrollments.AnyAsync(e => e.UserId == w.Parent));

            // Ventes de l'auteur posées, trace bénéficiaire pour l'écran « impact ».
            Assert.True(await db.WalletTransactions.AnyAsync(t => t.OwnerId == w.Author && t.EntryType == WalletEntryTypes.CatalogSale));
            Assert.Equal(2, await db.ParentCreditLedgers.CountAsync(l => l.OrderId == order.Id && l.ChildId == w.Child));
        }

        [Fact]
        public async Task Parent_InsufficientBalance_IsRefusedWithTheMissingAmount_AndNothingIsCreated()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 1000m);
            await FillCartAsync(db, w.Parent, (w.SubjectB, null, 2386m));

            var result = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });

            Assert.Equal(402, Status(result));
            Assert.Equal("insufficient_balance", Code(result));
            Assert.Equal(1386m, Body(result).GetProperty("missingXaf").GetDecimal());
            Assert.Empty(db.Orders.Where(o => o.UserId == w.Parent));
            Assert.Equal(1000m, await AvailableAsync(db, w.Parent));
            Assert.Single(db.CartItems.Where(c => c.UserId == w.Parent));
        }

        [Fact]
        public async Task Parent_ChildNotLinkedOrNotAccepted_IsRefused()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 10_000m);
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m));
            var controller = ParentController(db, w.Parent);

            var pending = await controller.PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.OtherChild });
            var stranger = await controller.PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Student });

            Assert.Equal(403, Status(pending));
            Assert.Equal("child_not_linked", Code(pending));
            Assert.Equal(403, Status(stranger));
            Assert.Empty(db.Orders);
            Assert.Equal(10_000m, await AvailableAsync(db, w.Parent));
        }

        [Fact]
        public async Task Parent_ContentAlreadyOwnedByTheChild_IsRefusedBeforeAnyDebit()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 10_000m);
            db.Enrollments.Add(new Enrollment { UserId = w.Child, SubjectId = w.SubjectA });
            await db.SaveChangesAsync();
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m), (w.SubjectB, null, 2386m));

            var result = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });

            Assert.Equal(409, Status(result));
            Assert.Equal("already_owned", Code(result));
            Assert.Contains(w.SubjectA, Body(result).GetProperty("subjectIds").EnumerateArray().Select(e => e.GetInt32()));
            Assert.Empty(db.Orders);
            Assert.Equal(10_000m, await AvailableAsync(db, w.Parent));
        }

        [Fact]
        public async Task Parent_DoubleSubmission_ReturnsTheSameOrder_WithoutASecondDebit()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 10_000m);
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m));
            var first = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });
            Assert.Equal(200, Status(first));

            // Le client repousse son panier local puis rejoue l'appel.
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m));
            var replay = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });

            Assert.Equal(200, Status(replay));
            Assert.True(Body(replay).GetProperty("duplicate").GetBoolean());
            Assert.Single(db.Orders.Where(o => o.UserId == w.Parent));
            Assert.Equal(10_000m - 1193m, await AvailableAsync(db, w.Parent));
            Assert.Empty(db.CartItems.Where(c => c.UserId == w.Parent));
        }

        [Fact]
        public async Task Parent_EnglishAccount_GetsEnglishErrors()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db, parentLocale: "en");
            var result = await ParentController(db, w.Parent).PayWithWallet(new ParentCartCheckoutRequest { ChildId = w.Child });
            Assert.Equal("cart_empty", Code(result));
            Assert.Equal("Your cart is empty.", Body(result).GetProperty("error").GetString());
        }

        // ── Parent : portefeuille + Mobile Money, et Mobile Money seul ────

        [Fact]
        public async Task Parent_Complement_DebitsTheWalletPart_ThenRestoresItWhenMobileMoneyFails()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 1000m);
            await FillCartAsync(db, w.Parent, (w.SubjectB, null, 2386m));
            var payments = Payments();

            var result = await ParentController(db, w.Parent, payments).PayWithWalletComplement(
                new ParentCartCheckoutRequest { ChildId = w.Child, Operator = "mtn", Phone = "670000000" });

            Assert.Equal(200, Status(result));
            var data = Body(result).GetProperty("data");
            Assert.Equal(1000m, data.GetProperty("balancePartXaf").GetDecimal());
            Assert.Equal(1386m, data.GetProperty("momoPartXaf").GetDecimal());
            payments.Verify(p => p.InitiateNotchPayAsync(w.Parent, It.Is<InitiatePaymentRequest>(r => r.Amount == 1386m)), Times.Once);
            Assert.Equal(0m, await AvailableAsync(db, w.Parent));
            var order = await db.Orders.SingleAsync(o => o.UserId == w.Parent);
            Assert.Equal("pending", order.Status);
            Assert.False(await db.Enrollments.AnyAsync(e => e.UserId == w.Child));

            // Double soumission pendant l'attente : aucun second débit ni second paiement.
            await FillCartAsync(db, w.Parent, (w.SubjectB, null, 2386m));
            var replay = await ParentController(db, w.Parent, payments).PayWithWalletComplement(
                new ParentCartCheckoutRequest { ChildId = w.Child, Operator = "mtn", Phone = "670000000" });
            Assert.Equal(409, Status(replay));
            Assert.Equal("payment_in_progress", Code(replay));
            payments.Verify(p => p.InitiateNotchPayAsync(It.IsAny<int?>(), It.IsAny<InitiatePaymentRequest>()), Times.Once);

            // Le complément échoue (opérateur, expiration) : part portefeuille restituée.
            order.Status = "failed";
            await db.SaveChangesAsync();
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            await wallet.SyncOrderAsync(order.Id);
            await wallet.SyncOrderAsync(order.Id); // rejeu : une seule restitution
            Assert.Equal(1000m, await AvailableAsync(db, w.Parent));
            Assert.False(await db.Enrollments.AnyAsync(e => e.UserId == w.Child));
        }

        [Fact]
        public async Task Parent_Complement_Confirmed_GivesTheChildAccess()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 1000m);
            await FillCartAsync(db, w.Parent, (w.SubjectB, null, 2386m));
            await ParentController(db, w.Parent).PayWithWalletComplement(
                new ParentCartCheckoutRequest { ChildId = w.Child, Operator = "orange", Phone = "690000000" });

            var order = await db.Orders.SingleAsync(o => o.UserId == w.Parent);
            order.Status = "completed"; // confirmation NotchPay
            await db.SaveChangesAsync();
            await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(order.Id);

            Assert.True(await db.Enrollments.AnyAsync(e => e.UserId == w.Child && e.SubjectId == w.SubjectB));
            Assert.Equal(0m, await AvailableAsync(db, w.Parent)); // part portefeuille acquise
        }

        [Fact]
        public async Task Parent_Complement_NotStarted_RestoresTheWalletImmediately()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 1000m);
            await FillCartAsync(db, w.Parent, (w.SubjectB, null, 2386m));

            var result = await ParentController(db, w.Parent, Payments(fail: true)).PayWithWalletComplement(
                new ParentCartCheckoutRequest { ChildId = w.Child, Operator = "mtn", Phone = "670000000" });

            Assert.Equal(503, Status(result));
            Assert.Equal("failed", (await db.Orders.SingleAsync(o => o.UserId == w.Parent)).Status);
            Assert.Equal(1000m, await AvailableAsync(db, w.Parent));
        }

        [Fact]
        public async Task Parent_MobileMoneyOnly_EvenWithEnoughBalance_DoesNotTouchTheWallet()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Parent, 10_000m);
            await FillCartAsync(db, w.Parent, (w.SubjectA, null, 1193m));
            var payments = Payments();

            var result = await ParentController(db, w.Parent, payments).PayMobileMoney(
                new ParentCartCheckoutRequest { ChildId = w.Child, Operator = "mtn", Phone = "670000000" });

            Assert.Equal(200, Status(result));
            payments.Verify(p => p.InitiateNotchPayAsync(w.Parent, It.Is<InitiatePaymentRequest>(r => r.Amount == 1193m)), Times.Once);
            Assert.Equal(10_000m, await AvailableAsync(db, w.Parent));
            var order = await db.Orders.SingleAsync(o => o.UserId == w.Parent);
            Assert.Equal(CourseSales.ChildMarkerPrefix + w.Child, order.Notes);
        }

        // ── Élève et professeur : leur propre portefeuille ───────────────

        [Fact]
        public async Task Student_SufficientBalance_PaysWithHisWallet()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Student, 5000m); // recharges Mobile Money
            await FillCartAsync(db, w.Student, (w.SubjectA, null, 1193m));

            var result = await Orders(db, w.Student, "student").PayWithBalance();

            Assert.Equal(200, Status(result));
            var order = await db.Orders.SingleAsync(o => o.UserId == w.Student);
            Assert.Equal("completed", order.Status);
            Assert.Equal("balance", order.PaymentMethod);
            Assert.Equal(5000m - 1193m, await AvailableAsync(db, w.Student));
            var access = new ContentAccessService(db, new Backend.Services.Entitlements.EntitlementService(db, new MemoryCache(new MemoryCacheOptions())));
            Assert.True(await access.HasPaidContentAccessAsync(w.Student, await db.Subjects.SingleAsync(s => s.Id == w.SubjectA), false));
        }

        [Fact]
        public async Task Student_InsufficientBalance_IsRefused_ThenTheComplementCombinesBothAndRestoresOnFailure()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Student, 500m);
            await FillCartAsync(db, w.Student, (w.SubjectB, null, 2386m));

            var refused = await Orders(db, w.Student, "student").PayWithBalance();
            Assert.Equal(402, Status(refused));
            Assert.Equal("insufficient_balance", Code(refused));
            Assert.Empty(db.Orders);

            var payments = Payments();
            var combined = await Orders(db, w.Student, "student", payments)
                .PayWithBalanceComplement(new PayWithBalanceComplementRequest("mtn", "670000000"));
            Assert.Equal(200, Status(combined));
            payments.Verify(p => p.InitiateNotchPayAsync(w.Student, It.Is<InitiatePaymentRequest>(r => r.Amount == 1886m)), Times.Once);
            Assert.Equal(0m, await AvailableAsync(db, w.Student));

            var order = await db.Orders.SingleAsync(o => o.UserId == w.Student);
            order.Status = "failed";
            await db.SaveChangesAsync();
            await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(order.Id);
            Assert.Equal(500m, await AvailableAsync(db, w.Student));
        }

        [Fact]
        public async Task Student_DoubleSubmission_DoesNotChargeTwice()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Student, 5000m);
            await FillCartAsync(db, w.Student, (w.SubjectA, null, 1193m));
            Assert.Equal(200, Status(await Orders(db, w.Student, "student").PayWithBalance()));

            await FillCartAsync(db, w.Student, (w.SubjectA, null, 1193m));
            var replay = await Orders(db, w.Student, "student").PayWithBalance();

            Assert.Equal(200, Status(replay));
            Assert.True(Body(replay).GetProperty("duplicate").GetBoolean());
            Assert.Single(db.Orders.Where(o => o.UserId == w.Student));
            Assert.Equal(5000m - 1193m, await AvailableAsync(db, w.Student));
        }

        [Fact]
        public async Task Teacher_StillPaysWithBalance_ParentIsSentToTheChildFlow()
        {
            using var db = TestApplicationDbContext.Create();
            var w = await SeedAsync(db);
            await CreditAsync(db, w.Teacher, 5000m);
            await FillCartAsync(db, w.Teacher, (w.SubjectA, null, 1193m));
            Assert.Equal(200, Status(await Orders(db, w.Teacher, "teacher").PayWithBalance()));

            var parent = await Orders(db, w.Parent, "parent").PayWithBalance();
            Assert.Equal(403, Status(parent));
            Assert.Equal("balance_not_available", Code(parent));
        }
    }
}
