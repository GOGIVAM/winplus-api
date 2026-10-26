using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Restes signalés à la livraison du 9.7b : limite de formations publiées,
    /// choix des enfants couverts, aperçu du catalogue servi limité à N pages,
    /// fonctions IA décomptées et droit course_builder.
    /// </summary>
    public partial class Restes97bTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"restes97b-{Guid.NewGuid()}";
        private const int Teacher = 9801, Parent = 9802, ChildA = 9803, ChildB = 9804, ChildC = 9805, Student = 9806, Admin = 9807;

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);
        private static EntitlementService Ent(TestApplicationDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()));

        public Restes97bTests()
        {
            using var db = Db();
            db.Users.AddRange(
                new User { Id = Teacher, Email = "t@r97b.local", Role = "teacher", FirstName = "Prof", LastName = "Test", IsEmailVerified = true },
                new User { Id = Parent, Email = "p@r97b.local", Role = "parent" },
                new User { Id = ChildA, Email = "a@r97b.local", Role = "student", FirstName = "Ana" },
                new User { Id = ChildB, Email = "b@r97b.local", Role = "student", FirstName = "Ben" },
                new User { Id = ChildC, Email = "c@r97b.local", Role = "student", FirstName = "Chloé" },
                new User { Id = Student, Email = "s@r97b.local", Role = "student" },
                new User { Id = Admin, Email = "admin@r97b.local", Role = "admin" });
            db.SaveChanges();
        }

        private static ClaimsPrincipal Principal(int userId, string role) =>
            new(new ClaimsIdentity(new[] { new Claim("user_id", userId.ToString()), new Claim(ClaimTypes.Role, role) }, "test"));

        private static void Bind(ControllerBase c, int userId, string role)
        {
            var http = new DefaultHttpContext { User = Principal(userId, role) };
            http.Request.Headers["Authorization"] = "Bearer test";
            c.ControllerContext = new ControllerContext { HttpContext = http };
        }

        private static int Status(IActionResult r) => r switch
        {
            ObjectResult o => o.StatusCode ?? 200,
            StatusCodeResult s => s.StatusCode,
            _ => 200,
        };

        private static string Json(IActionResult r) => JsonSerializer.Serialize(((ObjectResult)r).Value);

        // ── Portes IA (quota simulé) ──────────────────────────────────────────

        private sealed class QuotaSpy
        {
            public readonly Mock<IAiQuotaService> Mock = new();
            public readonly List<(int User, string Reservation, int Tokens)> Finalized = new();
            public readonly List<string> Released = new();
            public readonly List<string> Sources = new();

            public QuotaSpy(AiQuotaOutcome outcome = AiQuotaOutcome.Reserved)
            {
                Mock.Setup(q => q.CheckAndReserveAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int _, string? _, string source, CancellationToken _) =>
                    {
                        Sources.Add(source);
                        return new AiQuotaDecision(outcome, "res-1", "att-1",
                            outcome == AiQuotaOutcome.LimitReached ? AiLimitKind.Week : null, null);
                    });
                Mock.Setup(q => q.FinalizeUsageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int u, string r, int t, CancellationToken _) => Finalized.Add((u, r, t)))
                    .Returns(Task.CompletedTask);
                Mock.Setup(q => q.ReleaseReservationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Callback((int _, string r, string _, CancellationToken _) => Released.Add(r))
                    .Returns(Task.CompletedTask);
            }
        }

        private static AiFeatureGate Gate(IEntitlementService ent, QuotaSpy quota)
        {
            var fast = new Mock<IFastApiClient>();
            fast.Setup(f => f.ConsumeWinAiTokens()).Returns(0);
            return new AiFeatureGate(ent, quota.Mock.Object, fast.Object);
        }

        /// <summary>Réponse FastAPI simulée, avec l'en-tête de coût réel.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly string _body;
            private readonly int _tokens;
            public int Calls;
            public FakeHandler(string body, int tokens) { _body = body; _tokens = tokens; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
                res.Headers.Add(FastApiClient.WinAiTokensHeader, _tokens.ToString());
                return Task.FromResult(res);
            }
        }

        private static IHttpClientFactory Factory(FakeHandler handler)
        {
            var f = new Mock<IHttpClientFactory>();
            f.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://fastapi.test") });
            return f.Object;
        }

        private TeacherCourseController CourseController(TestApplicationDbContext db, EntitlementService ent, IAiFeatureGate gate, IHttpClientFactory? http = null) =>
            new(db, NullLogger<TeacherCourseController>.Instance, new Mock<ICourseGamificationService>().Object, new Mock<INtfyService>().Object,
                http ?? new Mock<IHttpClientFactory>().Object, new Mock<IFastApiClient>().Object, new Mock<ICatalogCommissionService>().Object,
                ent, gate);

        private static Course NewCourse(TestApplicationDbContext db, int instructor, string status, string title)
        {
            var c = new Course { Title = title, Slug = title.ToLowerInvariant().Replace(' ', '-') + Guid.NewGuid().ToString("N")[..6], InstructorId = instructor, Status = status, IsFree = true, Price = 0 };
            db.Courses.Add(c);
            db.SaveChanges();
            var section = new CourseSection { CourseId = c.Id, Title = "S1", Position = 0 };
            db.CourseSections.Add(section);
            db.SaveChanges();
            db.CourseLessons.Add(new CourseLesson { CourseId = c.Id, SectionId = section.Id, Title = "L1", LessonType = "article", IsPublished = true, Position = 0 });
            db.SaveChanges();
            return c;
        }

    }
}
