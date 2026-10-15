using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Repositories;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.7b, chantier B (suivi 18.16 et 18.21, étude section 4) : formations
    /// vendues seules, prix TTC, inscription créée par le serveur au paiement,
    /// pas de rachat, doublon par (type, id), complétion validée côté serveur.
    /// </summary>
    public class CourseSales97bTests
    {
        private static TestApplicationDbContext Fresh() => TestApplicationDbContext.Create();

        private sealed record World(int Teacher, int Student, int Parent, int Child, int CourseId, int FreeCourseId);

        private static async Task<World> SeedAsync(ApplicationDbContext db, decimal priceTtc = 5963m, decimal commissionRate = 0.2m)
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            var teacher = new User { Email = $"prof-{tag}@t.local", Role = "teacher", IsActive = true, FirstName = "Prof", LastName = "T" };
            var student = new User { Email = $"eleve-{tag}@t.local", Role = "student", IsActive = true, FirstName = "Eleve", LastName = "T" };
            var parent = new User { Email = $"parent-{tag}@t.local", Role = "parent", IsActive = true, FirstName = "Parent", LastName = "T" };
            var child = new User { Email = $"enfant-{tag}@t.local", Role = "student", IsActive = true, FirstName = "Enfant", LastName = "T" };
            db.Users.AddRange(teacher, student, parent, child);
            await db.SaveChangesAsync();
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted", InitiatedBy = parent.Id });
            var course = new Course
            {
                Title = $"Formation {tag}", Slug = $"f-{tag}", InstructorId = teacher.Id, Status = "published",
                Price = priceTtc, IsFree = false, PlatformCommissionRate = commissionRate,
            };
            var free = new Course { Title = $"Gratuite {tag}", Slug = $"g-{tag}", InstructorId = teacher.Id, Status = "published", Price = 0, IsFree = true };
            db.Courses.AddRange(course, free);
            await db.SaveChangesAsync();
            return new World(teacher.Id, student.Id, parent.Id, child.Id, course.Id, free.Id);
        }

        private static CartService Cart(ApplicationDbContext db) => new(
            new CartRepository(db, NullLogger<CartRepository>.Instance),
            new SubjectRepository(db, NullLogger<SubjectRepository>.Instance),
            new UserRepository(db, NullLogger<UserRepository>.Instance),
            db, NullLogger<CartService>.Instance);

        private static OrderService Orders(ApplicationDbContext db) => new(
            new OrderRepository(db, NullLogger<OrderRepository>.Instance),
            new CartRepository(db, NullLogger<CartRepository>.Instance),
            new SubjectRepository(db, NullLogger<SubjectRepository>.Instance),
            new PromoCodeService(db, NullLogger<PromoCodeService>.Instance),
            db, NullLogger<OrderService>.Instance);

        // ── Prix TTC (18.21, F11) ────────────────────────────────────────

        [Fact]
        public void Vat_IsComputedInsideTtcPrice_WorkedExample()
        {
            Assert.Equal(5000m, VatPolicy.ExclTaxFromInclTax(5963m));
            Assert.Equal(963m, VatPolicy.TaxIncludedIn(5963m));
            Assert.Equal(5963m, VatPolicy.LegacyTotalInclTax(5000m));
            Assert.Equal(0m, VatPolicy.ExclTaxFromInclTax(0m));
        }

        [Fact]
        public void Vat_MigrationRule_KeepsEveryFormerHtPriceExactly()
        {
            // Règle de migration : nouveau TTC = ancien HT + arrondi(HT × 19,25 %).
            // Le HT déduit du nouveau TTC doit redonner l'ancien HT, sans écart.
            for (var ht = 1m; ht <= 200_000m; ht += 1m)
            {
                var ttc = VatPolicy.LegacyTotalInclTax(ht);
                Assert.Equal(ht, VatPolicy.ExclTaxFromInclTax(ttc));
                Assert.Equal(ttc, VatPolicy.ExclTaxFromInclTax(ttc) + VatPolicy.TaxIncludedIn(ttc));
            }
        }

        // ── F1/F8 : Mobile Money confirmé → inscription créée, OrderId ───

        [Fact]
        public async Task MobileMoneyPurchase_CreatesEnrollmentOnConfirmation_AndLedgerSplitsTheDeducedHt()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            await Cart(db).AddCourseToCartAsync(w.Student, w.CourseId);
            var orders = Orders(db);
            var order = await orders.CreateOrderAsync(w.Student, "mtn");

            // TTC payé, pas de TVA ajoutée ; la ligne porte le HT déduit.
            Assert.Equal(5963m, order.TotalAmount);
            var item = Assert.Single(db.OrderItems.Where(i => i.OrderId == order.Id).ToList());
            Assert.Equal(5000m, item.PriceAtPurchase);

            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            await wallet.SyncOrderAsync(order.Id); // paiement en attente : pas d'accès
            Assert.False(await db.CourseEnrollments.AnyAsync(e => e.UserId == w.Student && e.CourseId == w.CourseId));

            await orders.UpdateOrderStatusAsync(order.Id, "completed"); // confirmation NotchPay
            await wallet.SyncOrderAsync(order.Id);
            await wallet.SyncOrderAsync(order.Id); // rejeu (webhook + consultation de statut)

            var enrollment = Assert.Single(db.CourseEnrollments.Where(e => e.UserId == w.Student && e.CourseId == w.CourseId).ToList());
            Assert.True(enrollment.IsActive);
            Assert.Equal("purchase", enrollment.AccessType);
            Assert.Equal(order.Id, enrollment.OrderId);

            // Écritures : net professeur + commission = HT déduit (5 000), TVA (963) hors partage.
            var entries = db.WalletTransactions.Where(t => t.SourceType == "OrderItem" && t.SourceId == item.Id).ToList();
            var net = Assert.Single(entries, t => t.EntryType == WalletEntryTypes.CatalogSale);
            var commission = Assert.Single(entries, t => t.EntryType == WalletEntryTypes.PlatformCommission);
            Assert.Equal(w.Teacher, net.OwnerId);
            Assert.Equal(4000m, net.Amount);
            Assert.Equal(1000m, commission.Amount);
            Assert.Equal(5000m, net.Amount + commission.Amount);
            Assert.Equal(963m, order.TotalAmount - (net.Amount + commission.Amount));
        }

        [Fact]
        public async Task RefundedOrder_DeactivatesTheEnrollmentItOpened()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            await Cart(db).AddCourseToCartAsync(w.Student, w.CourseId);
            var orders = Orders(db);
            var order = await orders.CreateOrderAsync(w.Student, "mtn");
            await orders.UpdateOrderStatusAsync(order.Id, "completed");
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            await wallet.SyncOrderAsync(order.Id);

            var tracked = await db.Orders.FirstAsync(o => o.Id == order.Id);
            tracked.Status = "refunded";
            await db.SaveChangesAsync();
            await wallet.SyncOrderAsync(order.Id);

            var enrollment = await db.CourseEnrollments.AsNoTracking().SingleAsync(e => e.UserId == w.Student && e.CourseId == w.CourseId);
            Assert.False(enrollment.IsActive);
        }

        // ── F3/F10 : pas de rachat ───────────────────────────────────────

        [Fact]
        public async Task NoRepurchase_AlreadyBought_OwnCourse_FreeCourse()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            var cart = Cart(db);
            await cart.AddCourseToCartAsync(w.Student, w.CourseId);
            var orders = Orders(db);
            var order = await orders.CreateOrderAsync(w.Student, "mtn");
            await orders.UpdateOrderStatusAsync(order.Id, "completed");
            await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(order.Id);

            var again = await Assert.ThrowsAsync<CoursePurchaseRefusedException>(() => cart.AddCourseToCartAsync(w.Student, w.CourseId));
            Assert.Equal("already_enrolled", again.Code);

            var own = await Assert.ThrowsAsync<CoursePurchaseRefusedException>(() => cart.AddCourseToCartAsync(w.Teacher, w.CourseId));
            Assert.Equal("own_course", own.Code);

            var free = await Assert.ThrowsAsync<CoursePurchaseRefusedException>(() => cart.AddCourseToCartAsync(w.Parent, w.FreeCourseId));
            Assert.Equal("free_course", free.Code);

            // Inscription désactivée mais commande toujours payée : « déjà achetée ».
            var enrollment = await db.CourseEnrollments.FirstAsync(e => e.UserId == w.Student && e.CourseId == w.CourseId);
            enrollment.IsActive = false;
            await db.SaveChangesAsync();
            var bought = await Assert.ThrowsAsync<CoursePurchaseRefusedException>(() => cart.AddCourseToCartAsync(w.Student, w.CourseId));
            Assert.Equal("already_purchased", bought.Code);
        }

        [Fact]
        public async Task NoRepurchase_IsAlsoCheckedAtOrderTime()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            // Panier rempli avant l'achat (autre appareil), puis achat ailleurs.
            db.CartItems.Add(new CartItem { UserId = w.Student, CourseId = w.CourseId, Price = 5963m });
            db.Orders.Add(new Order { OrderNumber = "ORD-X", UserId = w.Student, Status = "completed", TotalAmount = 5963m,
                Items = new List<OrderItem> { new() { CourseId = w.CourseId, PriceAtPurchase = 5000m } } });
            await db.SaveChangesAsync();

            var ex = await Assert.ThrowsAsync<CoursePurchaseRefusedException>(() => Orders(db).CreateOrderAsync(w.Student, "mtn"));
            Assert.Equal("already_purchased", ex.Code);
        }

        [Fact]
        public void FreeAndPrice_AreReconciled()
        {
            var a = new Course { IsFree = true, Price = 5000m };
            CourseSales.ReconcileFreeAndPrice(a);
            Assert.Equal(0m, a.Price);
            var b = new Course { IsFree = false, Price = 0m };
            CourseSales.ReconcileFreeAndPrice(b);
            Assert.True(b.IsFree);
            var c = new Course { IsFree = false, Price = 2500.4m };
            CourseSales.ReconcileFreeAndPrice(c);
            Assert.False(c.IsFree);
            Assert.Equal(2500m, c.Price);
        }

        // ── F4 : clé de doublon par (type, id) ───────────────────────────

        [Fact]
        public void DuplicateKey_DistinguishesTwoCoursesOfTheSamePrice()
        {
            var first = CourseSales.LinesKey(new (int?, int?)[] { (null, 1) });
            var second = CourseSales.LinesKey(new (int?, int?)[] { (null, 2) });
            Assert.NotEqual(first, second);
            Assert.NotEqual(CourseSales.LinesKey(new (int?, int?)[] { (7, null) }), CourseSales.LinesKey(new (int?, int?)[] { (null, 7) }));
            Assert.Equal(CourseSales.LinesKey(new (int?, int?)[] { (3, null), (null, 1) }), CourseSales.LinesKey(new (int?, int?)[] { (null, 1), (3, null) }));
        }

        // ── F6/F7 : formation retirée de l'abonnement ────────────────────

        [Fact]
        public async Task Enroll_PaidCourseFlaggedAsIncludedInSubscription_StillRequiresPurchase()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            var course = await db.Courses.FirstAsync(c => c.Id == w.CourseId);
            course.IsIncludedInSub = true; // ancienne donnée
            await db.SaveChangesAsync();

            var controller = Controller(new CourseEnrollmentController(db, NullLogger<CourseEnrollmentController>.Instance), w.Student, "student");
            var paid = await controller.Enroll(w.CourseId);
            Assert.IsType<BadRequestObjectResult>(paid);
            Assert.False(await db.CourseEnrollments.AnyAsync(e => e.UserId == w.Student && e.CourseId == w.CourseId));

            var free = await controller.Enroll(w.FreeCourseId);
            Assert.IsType<OkObjectResult>(free);
            var enrollment = await db.CourseEnrollments.SingleAsync(e => e.UserId == w.Student && e.CourseId == w.FreeCourseId);
            Assert.Equal("free", enrollment.AccessType);
        }

        // ── F14 : achat parent pour un enfant (synchronisation) ──────────

        [Fact]
        public async Task ParentOrderForChild_EnrollsTheChild_NotTheParent()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            var order = new Order
            {
                OrderNumber = "WP-CHILD", UserId = w.Parent, Status = "paid", PaymentMethod = "parent_wallet", TotalAmount = 5963m,
                Notes = CourseSales.ChildMarkerPrefix + w.Child,
                Items = new List<OrderItem> { new() { CourseId = w.CourseId, PriceAtPurchase = 5000m } },
            };
            db.Orders.Add(order);
            await db.SaveChangesAsync();

            await new WalletService(db, NullLogger<WalletService>.Instance).SyncOrderAsync(order.Id);

            var enrollment = await db.CourseEnrollments.SingleAsync(e => e.CourseId == w.CourseId);
            Assert.Equal(w.Child, enrollment.UserId);
            Assert.Equal(order.Id, enrollment.OrderId);
            Assert.True(await CourseSales.IsPurchasedAsync(db, w.Child, w.CourseId));
            Assert.False(await CourseSales.IsPurchasedAsync(db, w.Parent, w.CourseId));
        }

        // ── F12 : complétion validée par le serveur ──────────────────────

        [Fact]
        public void WatchTime_CannotJumpAheadOfRealElapsedTime()
        {
            var now = DateTime.UtcNow;
            // Ouverte il y a 10 s, l'élève déclare 3 600 s : crédité au plus 2 × 10 + marge.
            var credited = LessonCompletionRules.CreditedWatchTime(0, 3600, now.AddSeconds(-10), now);
            Assert.Equal(10 * LessonCompletionRules.MaxPlaybackRate + LessonCompletionRules.WatchSlackSec, credited);
            // Jamais de recul.
            Assert.Equal(500, LessonCompletionRules.CreditedWatchTime(500, 100, now.AddSeconds(-10), now));
        }

        [Fact]
        public async Task Completion_VideoNeedsRealViewing_QuizNeedsAPassedAttempt()
        {
            using var db = Fresh();
            var w = await SeedAsync(db);
            var now = DateTime.UtcNow;
            var video = new CourseLesson { CourseId = w.CourseId, Title = "Vidéo", LessonType = "video", VideoDurationSec = 600 };
            var progress = new LessonProgress { UserId = w.Student, CourseId = w.CourseId, CreatedAt = now.AddSeconds(-5), WatchTimeSec = 40 };

            var refused = await LessonCompletionRules.CheckAsync(db, w.Student, video, progress, now);
            Assert.NotNull(refused.Refusal);
            progress.WatchTimeSec = 480; // 80 %
            Assert.Null((await LessonCompletionRules.CheckAsync(db, w.Student, video, progress, now)).Refusal);

            var quiz = new Quiz { Title = "Q", Subject = "Maths", PassingScore = 60, QuestionsJson = "[]" };
            db.Quizzes.Add(quiz);
            await db.SaveChangesAsync();
            var quizLesson = new CourseLesson { CourseId = w.CourseId, Title = "Quiz", LessonType = "quiz", QuizId = quiz.Id };
            Assert.NotNull((await LessonCompletionRules.CheckAsync(db, w.Student, quizLesson, progress, now)).Refusal);

            db.QuizAttempts.Add(new QuizAttempt { UserId = w.Student, QuizId = quiz.Id, Score = 40, Passed = false, UserAnswersJson = "{}" });
            await db.SaveChangesAsync();
            var failed = await LessonCompletionRules.CheckAsync(db, w.Student, quizLesson, progress, now);
            Assert.NotNull(failed.Refusal);

            db.QuizAttempts.Add(new QuizAttempt { UserId = w.Student, QuizId = quiz.Id, Score = 90, Passed = true, UserAnswersJson = "{}" });
            await db.SaveChangesAsync();
            var passed = await LessonCompletionRules.CheckAsync(db, w.Student, quizLesson, progress, now);
            Assert.Null(passed.Refusal);
            Assert.Equal(90, passed.QuizScorePercent); // score relu côté serveur

            var article = new CourseLesson { CourseId = w.CourseId, Title = "Article", LessonType = "article" };
            var justOpened = new LessonProgress { UserId = w.Student, CourseId = w.CourseId, CreatedAt = now.AddSeconds(-2) };
            Assert.NotNull((await LessonCompletionRules.CheckAsync(db, w.Student, article, justOpened, now)).Refusal);
        }

        internal static T Controller<T>(T controller, int userId, string role) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim("user_id", userId.ToString()), new Claim("role", role), new Claim(ClaimTypes.Role, role),
                    }, "test")),
                },
            };
            return controller;
        }
    }

    /// <summary>
    /// Lot 9.7b sur un vrai PostgreSQL (WINPLUS_TEST_PG) : paiement par solde
    /// (transaction Serializable, verrou consultatif), achat parent pour un
    /// enfant, et script de migration SQL_CourseSalesTtc97b.sql rejoué.
    /// </summary>
    public class CourseSales97bPostgresTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;

        public CourseSales97bPostgresTests(PostgresTestDatabase pg) => _pg = pg;

        private async Task<(int Teacher, int Buyer, int Parent, int Child, int CourseA, int CourseB)> SeedAsync()
        {
            var tag = Guid.NewGuid().ToString("N")[..8];
            await using var db = _pg.CreateContext();
            var teacher = new User { Email = $"auteur-{tag}@it.local", Role = "teacher", IsActive = true, FirstName = "A", LastName = "T" };
            var buyer = new User { Email = $"profacheteur-{tag}@it.local", Role = "teacher", IsActive = true, FirstName = "B", LastName = "T" };
            var parent = new User { Email = $"parent-{tag}@it.local", Role = "parent", IsActive = true, FirstName = "P", LastName = "T" };
            var child = new User { Email = $"enfant-{tag}@it.local", Role = "student", IsActive = true, FirstName = "E", LastName = "T" };
            db.Users.AddRange(teacher, buyer, parent, child);
            await db.SaveChangesAsync();
            db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted", InitiatedBy = parent.Id });
            var a = new Course { Title = $"A {tag}", Slug = $"a-{tag}", InstructorId = teacher.Id, Status = "published", Price = 5963m, PlatformCommissionRate = 0.2m };
            var b = new Course { Title = $"B {tag}", Slug = $"b-{tag}", InstructorId = teacher.Id, Status = "published", Price = 5963m, PlatformCommissionRate = 0.2m };
            db.Courses.AddRange(a, b);
            await db.SaveChangesAsync();
            return (teacher.Id, buyer.Id, parent.Id, child.Id, a.Id, b.Id);
        }

        /// <summary>
        /// Contrôleur parent construit par injection : son constructeur évolue
        /// avec le chantier A du 9.7b (droits effectifs). Les droits réels sont
        /// fournis (parent sans abonnement, aucune dotation).
        /// </summary>
        private static ParentCreditsController ParentController(ApplicationDbContext db)
        {
            var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(db);
            services.AddSingleton<Microsoft.Extensions.Logging.ILogger<ParentCreditsController>>(NullLogger<ParentCreditsController>.Instance);
            services.AddSingleton<INtfyService>(new Mock<INtfyService>().Object);
            services.AddSingleton<IWalletService>(new WalletService(db, NullLogger<WalletService>.Instance));
            services.AddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache>(new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()));
            services.AddSingleton<Backend.Services.Entitlements.IEntitlementService>(sp =>
                new Backend.Services.Entitlements.EntitlementService(db, sp.GetRequiredService<Microsoft.Extensions.Caching.Memory.IMemoryCache>()));
            using var provider = services.BuildServiceProvider();
            return Microsoft.Extensions.DependencyInjection.ActivatorUtilities.CreateInstance<ParentCreditsController>(provider);
        }
        private async Task CreditAsync(int owner, decimal amount)
        {
            await using var db = _pg.CreateContext();
            await new WalletService(db, NullLogger<WalletService>.Instance)
                .PostAsync(new WalletEntry(owner, WalletEntryTypes.Recharge, amount, $"it-seed-{owner}-{Guid.NewGuid()}", "Recharge de test"));
        }

        private static OrdersController BuildOrders(ApplicationDbContext db, int userId)
        {
            var wallet = new WalletService(db, NullLogger<WalletService>.Instance);
            var orderService = new OrderService(
                new OrderRepository(db, NullLogger<OrderRepository>.Instance),
                new CartRepository(db, NullLogger<CartRepository>.Instance),
                new SubjectRepository(db, NullLogger<SubjectRepository>.Instance),
                new PromoCodeService(db, NullLogger<PromoCodeService>.Instance),
                db, NullLogger<OrderService>.Instance);
            var controller = new OrdersController(orderService, NullLogger<OrdersController>.Instance, db,
                new Mock<IPdfService>().Object, new Mock<ITeacherService>().Object, new Mock<IAffiliateService>().Object,
                wallet, new Mock<IPaymentService>().Object);
            return CourseSales97bTests.Controller(controller, userId, "teacher");
        }

        [PostgresFact]
        public async Task BalancePurchase_CreatesEnrollment_AndASecondCourseOfTheSamePriceIsNotADuplicate()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Buyer, 20_000m);

            foreach (var courseId in new[] { w.CourseA, w.CourseB })
            {
                await using var db = _pg.CreateContext();
                db.CartItems.Add(new CartItem { UserId = w.Buyer, CourseId = courseId, Price = 5963m });
                await db.SaveChangesAsync();
                var result = await BuildOrders(db, w.Buyer).PayWithBalance();
                var ok = Assert.IsType<OkObjectResult>(result);
                Assert.DoesNotContain("duplicate", ok.Value!.ToString());
            }

            await using var check = _pg.CreateContext();
            var enrollments = await check.CourseEnrollments.Where(e => e.UserId == w.Buyer).ToListAsync();
            Assert.Equal(2, enrollments.Count);
            Assert.All(enrollments, e => { Assert.True(e.IsActive); Assert.Equal("purchase", e.AccessType); Assert.NotNull(e.OrderId); });
            var orders = await check.Orders.Where(o => o.UserId == w.Buyer).ToListAsync();
            Assert.Equal(2, orders.Count);
            Assert.All(orders, o => Assert.Equal(5963m, o.TotalAmount));
            // 2 × 5 963 débités, pas un de plus.
            var available = await new WalletService(check, NullLogger<WalletService>.Instance).GetAvailableAsync(w.Buyer);
            Assert.Equal(20_000m - 2 * 5963m, available);

            // Rachat de la même formation : refusé (409), rien n'est débité.
            await using (var db = _pg.CreateContext())
            {
                db.CartItems.Add(new CartItem { UserId = w.Buyer, CourseId = w.CourseA, Price = 5963m });
                await db.SaveChangesAsync();
                Assert.IsType<ConflictObjectResult>(await BuildOrders(db, w.Buyer).PayWithBalance());
            }
            await using var after = _pg.CreateContext();
            Assert.Equal(20_000m - 2 * 5963m, await new WalletService(after, NullLogger<WalletService>.Instance).GetAvailableAsync(w.Buyer));
        }

        [PostgresFact]
        public async Task ParentBuysCourseForChild_FromWallet_ChildIsEnrolled_TeacherPaidOnHt()
        {
            var w = await SeedAsync();
            await CreditAsync(w.Parent, 10_000m);

            await using (var db = _pg.CreateContext())
            {
                var controller = CourseSales97bTests.Controller(ParentController(db), w.Parent, "parent");
                var result = await controller.PurchaseForChild(new PurchaseForChildRequest { ChildId = w.Child, CourseId = w.CourseA });
                Assert.IsType<OkObjectResult>(result);
            }

            await using var check = _pg.CreateContext();
            var enrollment = await check.CourseEnrollments.SingleAsync(e => e.CourseId == w.CourseA);
            Assert.Equal(w.Child, enrollment.UserId);
            Assert.True(enrollment.IsActive);
            var order = await check.Orders.SingleAsync(o => o.Id == enrollment.OrderId);
            Assert.Equal(5963m, order.TotalAmount);
            var item = await check.OrderItems.SingleAsync(i => i.OrderId == order.Id);
            Assert.Equal(5000m, item.PriceAtPurchase);
            var sale = await check.WalletTransactions.Where(t => t.SourceType == "OrderItem" && t.SourceId == item.Id).SumAsync(t => t.Amount);
            Assert.Equal(5000m, sale); // net professeur + commission = HT déduit
            Assert.Equal(10_000m - 5963m, await new WalletService(check, NullLogger<WalletService>.Instance).GetAvailableAsync(w.Parent));

            // Seconde tentative pour le même enfant : refusée.
            await using (var db = _pg.CreateContext())
            {
                var controller = CourseSales97bTests.Controller(ParentController(db), w.Parent, "parent");
                var again = await controller.PurchaseForChild(new PurchaseForChildRequest { ChildId = w.Child, CourseId = w.CourseA });
                // Fenêtre d'idempotence (60 s) : la commande déjà payée est renvoyée, sans second débit.
                Assert.IsType<OkObjectResult>(again);
            }
            await using var final = _pg.CreateContext();
            Assert.Equal(1, await final.Orders.CountAsync(o => o.UserId == w.Parent));
        }

        [PostgresFact]
        public async Task MigrationScript_ConvertsPricesOnce_AndRepairsEnrollments()
        {
            var w = await SeedAsync();
            int subscriptionEnrollmentId, paidOrderId;
            await using (var db = _pg.CreateContext())
            {
                var c = await db.Courses.FirstAsync(x => x.Id == w.CourseB);
                c.Price = 5000m; c.IsIncludedInSub = true; // prix HT d'avant le 9.7b
                var a = await db.Courses.FirstAsync(x => x.Id == w.CourseA);
                a.Price = 5000m;
                await db.SaveChangesAsync();
                // Inscription « subscription » sans achat, et achat payé sans inscription.
                var sub = new CourseEnrollment { UserId = w.Buyer, CourseId = w.CourseB, AccessType = "subscription", IsActive = true };
                db.CourseEnrollments.Add(sub);
                var order = new Order { OrderNumber = $"MIG-{Guid.NewGuid():N}"[..20], UserId = w.Parent, Status = "paid", TotalAmount = 5000m,
                    Notes = CourseSales.ChildMarkerPrefix + w.Child,
                    Items = new List<OrderItem> { new() { CourseId = w.CourseA, PriceAtPurchase = 5000m } } };
                db.Orders.Add(order);
                await db.SaveChangesAsync();
                subscriptionEnrollmentId = sub.Id; paidOrderId = order.Id;
            }

            var sql = await File.ReadAllTextAsync(FindMigration("SQL_CourseSalesTtc97b.sql"));
            await using (var db = _pg.CreateContext())
            {
                await db.Database.ExecuteSqlRawAsync(sql);
                await db.Database.ExecuteSqlRawAsync(sql); // idempotence : pas de seconde majoration
            }

            await using var check = _pg.CreateContext();
            Assert.Equal(5963m, (await check.Courses.SingleAsync(c => c.Id == w.CourseA)).Price);
            var b = await check.Courses.SingleAsync(c => c.Id == w.CourseB);
            Assert.Equal(5963m, b.Price);
            Assert.False(b.IsIncludedInSub);
            Assert.False((await check.CourseEnrollments.SingleAsync(e => e.Id == subscriptionEnrollmentId)).IsActive);
            var repaired = await check.CourseEnrollments.SingleAsync(e => e.UserId == w.Child && e.CourseId == w.CourseA);
            Assert.True(repaired.IsActive);
            Assert.Equal(paidOrderId, repaired.OrderId);
        }

        private static string FindMigration(string file)
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            {
                var candidate = Path.Combine(dir.FullName, "dotnet", "Migrations", file);
                if (File.Exists(candidate)) return candidate;
            }
            throw new FileNotFoundException(file);
        }
    }
}
