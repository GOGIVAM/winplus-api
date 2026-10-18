using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 9.6 (suivi 17.5) : support de séance WinAI. Génération débitée sur
    /// le budget du tuteur et refusée sans le droit, accès limité aux
    /// participants et au payeur, partage, passage du quiz par un élève et
    /// remontée au tuteur, corrigés masqués avant réponse.
    /// </summary>
    public class TutorSessionMaterialTests
    {
        private static readonly InMemoryDatabaseRoot Root = new();
        private readonly string _name = $"material96-{Guid.NewGuid()}";
        private const int Tutor = 9601, Parent = 9602, ChildA = 9603, ChildB = 9604, Stranger = 9605, OtherTutor = 9606;
        private readonly Mock<INtfyService> _ntfy = new();

        private TestApplicationDbContext Db() => TestApplicationDbContext.Create(_name, Root);
        private static EntitlementService Ent(TestApplicationDbContext db) => new(db, new MemoryCache(new MemoryCacheOptions()));

        private const string Generated = @"{
          ""title"": ""Fractions"", ""objectives"": [""Additionner des fractions""],
          ""course"": { ""introduction"": ""Une fraction..."", ""sections"": [ { ""title"": ""Dénominateur commun"", ""content"": ""On réduit..."" } ],
                       ""summary"": ""Résumé"", ""keyPoints"": [""Même dénominateur""] },
          ""quiz"": [
            { ""id"": ""x1"", ""text"": ""1/2 + 1/2 ?"", ""options"": [ { ""id"": ""a"", ""text"": ""1"" }, { ""id"": ""b"", ""text"": ""2/4"" } ], ""correctOptionId"": ""a"", ""explanation"": ""2/2 = 1"" },
            { ""id"": ""x2"", ""text"": ""1/3 + 1/3 ?"", ""options"": [ { ""id"": ""a"", ""text"": ""2/6"" }, { ""id"": ""b"", ""text"": ""2/3"" } ], ""correctOptionId"": ""b"", ""explanation"": ""Même dénominateur"" },
            { ""id"": ""x3"", ""text"": ""Sans bonne réponse"", ""options"": [ { ""id"": ""a"", ""text"": ""?"" }, { ""id"": ""b"", ""text"": ""!"" } ], ""correctOptionId"": ""z"" }
          ],
          ""exercises"": [ { ""id"": ""e9"", ""title"": ""Calcul"", ""statement"": ""Calcule 3/4 + 1/8"", ""difficulty"": ""moyen"", ""hints"": [""Dénominateur 8""], ""solution"": ""7/8"" } ],
          ""groupNote"": ""Pour le groupe""
        }";

        public TutorSessionMaterialTests()
        {
            using var db = Db();
            db.Users.AddRange(
                new User { Id = Tutor, Email = "tutor@m96.local", Role = "teacher", FirstName = "Tina", LastName = "Tutrice" },
                new User { Id = OtherTutor, Email = "other@m96.local", Role = "teacher" },
                new User { Id = Parent, Email = "parent@m96.local", Role = "parent" },
                new User { Id = ChildA, Email = "a@m96.local", Role = "student", FirstName = "Ana", Level = "3e" },
                new User { Id = ChildB, Email = "b@m96.local", Role = "student", FirstName = "Ben", Level = "4e" },
                new User { Id = Stranger, Email = "s@m96.local", Role = "student" });
            db.TutorProfiles.Add(new TutorProfile { UserId = Tutor, IsActive = true, HourlyRateXaf = 5000 });
            db.SaveChanges();
        }

        private int SeedBooking(string status = "confirmed")
        {
            using var db = Db();
            var profileId = db.TutorProfiles.Single(p => p.UserId == Tutor).Id;
            var b = new TutorBooking
            {
                TutorProfileId = profileId, StudentUserId = ChildA, Subject = "Mathématiques",
                SessionDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(2)), StartTime = new TimeSpan(14, 0, 0), EndTime = new TimeSpan(15, 0, 0),
                Mode = "online", PriceXaf = 8000, Status = status, PaymentStatus = "completed",
                PayerUserId = Parent, PayerType = TutorPayerTypes.Parent, BookedByUserId = Parent, ParticipantCount = 2,
                Topics = new List<TutorBookingTopic> { new() { Label = "Fractions" } },
                Participants = new List<TutorBookingParticipant> { new() { StudentUserId = ChildA }, new() { StudentUserId = ChildB } },
            };
            db.TutorBookings.Add(b);
            db.SaveChanges();
            return b.Id;
        }

        private sealed class QuotaSpy
        {
            public readonly Mock<IAiQuotaService> Mock = new();
            public readonly List<(int User, int Tokens)> Finalized = new();
            public readonly List<int> Reserved = new();
            public QuotaSpy()
            {
                Mock.Setup(q => q.CheckAndReserveAsync(It.IsAny<int>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync((int u, string? _, string _, CancellationToken _) =>
                    {
                        Reserved.Add(u);
                        return new AiQuotaDecision(AiQuotaOutcome.Reserved, "res-1", "att-1", null, null);
                    });
                Mock.Setup(q => q.FinalizeUsageAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .Callback((int u, string _, int t, CancellationToken _) => Finalized.Add((u, t)))
                    .Returns(Task.CompletedTask);
                Mock.Setup(q => q.ReleaseReservationAsync(It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                    .Returns(Task.CompletedTask);
            }
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly string _body;
            private readonly int _tokens;
            public int Calls;
            public string? LastPath;
            public string? LastBody;
            public string? LastAuth;
            public FakeHandler(string body, int tokens) { _body = body; _tokens = tokens; }
            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                LastPath = request.RequestUri?.AbsolutePath;
                LastBody = request.Content == null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
                LastAuth = request.Headers.Authorization?.ToString();
                var res = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
                res.Headers.Add(FastApiClient.WinAiTokensHeader, _tokens.ToString());
                return res;
            }
        }

        private (TutorSessionMaterialService Service, QuotaSpy Quota, FakeHandler Http, EntitlementService Ent) Build(TestApplicationDbContext db, string body = Generated, int tokens = 3456)
        {
            var ent = Ent(db);
            var quota = new QuotaSpy();
            var fast = new Mock<IFastApiClient>();
            fast.Setup(f => f.ConsumeWinAiTokens()).Returns(0);
            var gate = new AiFeatureGate(ent, quota.Mock.Object, fast.Object);
            var handler = new FakeHandler(body, tokens);
            var factory = new Mock<IHttpClientFactory>();
            factory.Setup(x => x.CreateClient(It.IsAny<string>())).Returns(() => new HttpClient(handler, disposeHandler: false) { BaseAddress = new Uri("http://fastapi.test") });
            var service = new TutorSessionMaterialService(db, gate, factory.Object, _ntfy.Object, NullLogger<TutorSessionMaterialService>.Instance);
            return (service, quota, handler, ent);
        }

        private static int Status(IActionResult r) => r is ObjectResult o ? o.StatusCode ?? 200 : 200;

        // ── Génération : droit et budget du tuteur ───────────────────────────

        [Fact]
        public async Task Generation_IsDebitedOnTheTutorBudget_WithRealTokens()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, quota, http, _) = Build(db);

            var (refusal, view) = await service.GenerateAsync(Tutor, id, "Plus d'exemples", "Bearer tutor-token");

            Assert.Null(refusal);
            Assert.NotNull(view);
            Assert.Equal(1, http.Calls);
            Assert.Equal(TutorSessionMaterialService.FastApiRoute, http.LastPath);
            Assert.Equal("Bearer tutor-token", http.LastAuth);
            using (var payload = JsonDocument.Parse(http.LastBody!))
            {
                Assert.Equal(id, payload.RootElement.GetProperty("booking_id").GetInt32());
                Assert.Equal(2, payload.RootElement.GetProperty("participant_count").GetInt32());
                Assert.Equal("Fractions", payload.RootElement.GetProperty("topics")[0].GetString());
                Assert.Equal(60, payload.RootElement.GetProperty("duration_minutes").GetInt32());
            }
            // Réserve et débit sur le compte du TUTEUR, au coût réel annoncé par FastAPI.
            Assert.Equal(new[] { Tutor }, quota.Reserved);
            Assert.Equal(new[] { (Tutor, 3456) }, quota.Finalized);

            Assert.Equal("tutor", view!.ViewerRole);
            Assert.Equal("draft", view.Status);
            Assert.True(view.SolutionsIncluded);
            // Nettoyage : identifiants renumérotés, question sans bonne réponse écartée.
            Assert.Equal(new[] { "q1", "q2" }, view.Content!.Quiz.Select(q => q.Id));
            Assert.Equal("e1", view.Content.Exercises.Single().Id);
            Assert.Equal("7/8", view.Content.Exercises.Single().Solution);
            Assert.Equal(1, view.GenerationCount);

            // Relance : nouvelle version de la copie de travail, nouveau débit.
            var (_, again) = await service.GenerateAsync(Tutor, id, null, "Bearer tutor-token");
            Assert.Equal(2, again!.Version);
            Assert.Equal(2, again.GenerationCount);
            Assert.Equal(2, quota.Finalized.Count);
        }

        [Fact]
        public async Task Generation_RefusedWithoutTheEntitlement_WithoutCallingWinAi()
        {
            using var db = Db();
            var grid = Subscriptions97bTestGrid.Seed(db);
            // L'admin ferme le droit sur le plan gratuit professeur.
            db.PlanEntitlements.Single(e => e.PlanVersionId == grid["teacher-free"].VersionId && e.Key == EntitlementCatalog.AiSessionMaterial).Value = "false";
            db.SaveChanges();
            var id = SeedBooking();
            var (service, quota, http, _) = Build(db);

            var (refusal, view) = await service.GenerateAsync(Tutor, id, null, "Bearer t");

            Assert.Null(view);
            Assert.Equal(403, Status(refusal!));
            Assert.Contains("feature_not_in_plan", JsonSerializer.Serialize(((ObjectResult)refusal!).Value));
            Assert.Equal(0, http.Calls);
            Assert.Empty(quota.Reserved);
            Assert.False(await db.TutorSessionMaterials.AnyAsync());
        }

        [Fact]
        public async Task Generation_OnlyByTheBookingTutor_AndNotForACancelledSession()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, quota, http, _) = Build(db);

            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.GenerateAsync(OtherTutor, id, null, null));
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.GenerateAsync(ChildA, id, null, null));

            var cancelled = SeedBooking("cancelled");
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync(Tutor, cancelled, null, null));
            Assert.Equal(0, http.Calls);
            Assert.Empty(quota.Reserved);
        }

        [Fact]
        public async Task Controller_ReturnsThe403OfThePlan()
        {
            using var db = Db();
            var grid = Subscriptions97bTestGrid.Seed(db);
            db.PlanEntitlements.Single(e => e.PlanVersionId == grid["teacher-free"].VersionId && e.Key == EntitlementCatalog.AiSessionMaterial).Value = "false";
            db.SaveChanges();
            var id = SeedBooking();
            var (service, _, _, _) = Build(db);
            var ctl = new TutorSessionMaterialController(service);
            var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", Tutor.ToString()), new Claim(ClaimTypes.Role, "teacher") }, "test")) };
            ctl.ControllerContext = new ControllerContext { HttpContext = http };

            Assert.Equal(403, Status(await ctl.Generate(id, new GenerateTutorSessionMaterialRequestDto(), CancellationToken.None)));
        }

        // ── Accès, partage, corrigés masqués ────────────────────────────────

        [Fact]
        public async Task Access_LimitedToTutorParticipantsAndPayer_AndDraftStaysPrivate()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, _, _, _) = Build(db);
            await service.GenerateAsync(Tutor, id, null, null);

            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.GetAsync(Stranger, id));
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.GetAsync(OtherTutor, id));

            // Brouillon : ni l'élève ni le payeur ne le voient.
            var childDraft = await service.GetAsync(ChildA, id);
            Assert.Equal("participant", childDraft.ViewerRole);
            Assert.False(childDraft.Exists);
            Assert.Null(childDraft.Content);
            var parentDraft = await service.GetAsync(Parent, id);
            Assert.Equal("payer", parentDraft.ViewerRole);
            Assert.False(parentDraft.Exists);

            // Un élève ne partage ni ne modifie.
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.ShareAsync(ChildA, id));
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.UpdateAsync(Parent, id, new TutorSessionMaterialContent { Title = "x" }));
        }

        [Fact]
        public async Task Share_NotifiesParticipantsAndPayer_SolutionsHiddenUntilAnswered()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, _, _, _) = Build(db);
            await service.GenerateAsync(Tutor, id, null, null);

            var shared = await service.ShareAsync(Tutor, id);
            Assert.Equal("shared", shared.Status);
            Assert.False(shared.HasUnsharedChanges);
            Assert.False(shared.CanShare);
            foreach (var u in new[] { ChildA, ChildB, Parent })
                _ns(u, Times.Once());
            _ns(Tutor, Times.Never());

            var child = await service.GetAsync(ChildB, id);
            Assert.True(child.Exists);
            Assert.True(child.CanSubmit);
            Assert.False(child.SolutionsIncluded);
            Assert.All(child.Content!.Quiz, q => { Assert.Null(q.CorrectOptionId); Assert.Null(q.Explanation); });
            Assert.All(child.Content.Exercises, e => Assert.Null(e.Solution));
            Assert.Equal("Dénominateur commun", child.Content.Course.Sections.Single().Title);

            var parent = await service.GetAsync(Parent, id);
            Assert.True(parent.Exists);
            Assert.False(parent.CanSubmit);
            Assert.All(parent.Content!.Quiz, q => Assert.Null(q.CorrectOptionId));

            // Modification après partage : les élèves gardent la version partagée.
            var edited = shared.Content!;
            edited.Title = "Fractions (revu)";
            var draft = await service.UpdateAsync(Tutor, id, edited);
            Assert.True(draft.HasUnsharedChanges);
            Assert.Equal("Fractions", (await service.GetAsync(ChildA, id)).Content!.Title);
        }

        private void _ns(int userId, Times times) =>
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{userId}", It.Is<string>(t => t.StartsWith("Support de séance")), It.IsAny<string>(),
                It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), times);

        [Fact]
        public async Task Share_RefusedBeforeTheTutorAcceptedTheSession()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking("pending_tutor_approval");
            var (service, _, _, _) = Build(db);
            var (_, view) = await service.GenerateAsync(Tutor, id, null, null); // préparation possible avant d'accepter
            Assert.False(view!.CanShare);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ShareAsync(Tutor, id));
        }

        // ── Quiz : passage par un élève et remontée au tuteur ───────────────

        [Fact]
        public async Task Student_TakesTheQuiz_ScoreAndAnswersReachTheTutor()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, _, _, _) = Build(db);
            await service.GenerateAsync(Tutor, id, null, null);
            await service.ShareAsync(Tutor, id);

            var after = await service.SubmitAsync(ChildA, id, new SubmitTutorSessionMaterialRequestDto
            {
                QuizAnswers = new() { ["q1"] = "a", ["q2"] = "a", ["q99"] = "a" },
                ExerciseAnswers = new() { ["e1"] = "7/8", ["eX"] = "ignoré" },
            });

            Assert.Equal(1, after.MyResult!.QuizScore);
            Assert.Equal(2, after.MyResult.QuizTotal);
            Assert.True(after.SolutionsIncluded);
            Assert.False(after.CanSubmit);
            Assert.Equal("a", after.Content!.Quiz[0].CorrectOptionId); // correction visible après envoi
            Assert.Contains(after.MyResult.Questions, q => q.QuestionId == "q2" && !q.IsCorrect && q.CorrectOptionId == "b");
            Assert.Equal(new[] { "e1" }, after.MyResult.ExerciseAnswers.Keys);

            // Remontée au tuteur : notification et résultat détaillé.
            _ntfy.Verify(n => n.PublishAsync($"winplus-user-{Tutor}", "Support de séance : réponses reçues", It.Is<string>(m => m.Contains("1/2")),
                It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<int?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once());
            var tutorView = await service.GetAsync(Tutor, id);
            var result = tutorView.Results.Single();
            Assert.Equal(ChildA, result.StudentUserId);
            Assert.Equal(1, result.QuizScore);
            Assert.Equal("7/8", result.ExerciseAnswers["e1"]);
            Assert.NotNull((await db.TutorSessionMaterialResults.AsNoTracking().SingleAsync()).SeenByTutorAt);

            // Le parent payeur voit la note de son enfant, sans le détail ni les corrigés.
            var parent = await service.GetAsync(Parent, id);
            Assert.Equal(1, parent.Results.Single().QuizScore);
            Assert.Empty(parent.Results.Single().Questions);
            Assert.Empty(parent.Results.Single().ExerciseAnswers);

            // Un seul envoi par version ; le payeur et un inconnu ne répondent pas.
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(ChildA, id, new SubmitTutorSessionMaterialRequestDto { QuizAnswers = new() { ["q1"] = "a" } }));
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.SubmitAsync(Parent, id, new SubmitTutorSessionMaterialRequestDto { QuizAnswers = new() { ["q1"] = "a" } }));
            await Assert.ThrowsAsync<TutorSessionMaterialForbiddenException>(() => service.SubmitAsync(Stranger, id, new SubmitTutorSessionMaterialRequestDto { QuizAnswers = new() { ["q1"] = "a" } }));

            // L'autre élève n'a pas encore répondu : corrigés toujours masqués pour lui.
            Assert.Null((await service.GetAsync(ChildB, id)).Content!.Quiz[0].CorrectOptionId);

            // Nouvelle version partagée : le quiz peut être refait.
            var content = tutorView.Content!;
            content.Title = "Fractions v2";
            await service.UpdateAsync(Tutor, id, content);
            await service.ShareAsync(Tutor, id);
            var redo = await service.GetAsync(ChildA, id);
            Assert.True(redo.CanSubmit);
            Assert.True(redo.MyResult!.IsOutdated);
        }

        [Fact]
        public async Task Submit_RefusedBeforeSharing()
        {
            using var db = Db();
            Subscriptions97bTestGrid.Seed(db);
            var id = SeedBooking();
            var (service, _, _, _) = Build(db);
            await service.GenerateAsync(Tutor, id, null, null);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.SubmitAsync(ChildA, id, new SubmitTutorSessionMaterialRequestDto { QuizAnswers = new() { ["q1"] = "a" } }));
        }

        [Fact]
        public async Task Settings_AutoGenerationIsOffByDefault()
        {
            using var db = Db();
            var (service, _, _, _) = Build(db);
            Assert.False((await service.GetSettingsAsync(Tutor)).AutoGenerateOnAccept);
            Assert.True((await service.SetSettingsAsync(Tutor, true)).AutoGenerateOnAccept);
            Assert.True(db.TutorProfiles.AsNoTracking().Single(p => p.UserId == Tutor).AutoGenerateSessionMaterial);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetSettingsAsync(Parent));
        }

        [Fact]
        public void Sanitize_BoundsAndIds()
        {
            var c = new TutorSessionMaterialContent
            {
                Title = new string('t', 500),
                Quiz = Enumerable.Range(0, 30).Select(i => new TutorSessionMaterialQuizQuestion
                {
                    Id = "same", Text = $"Q{i}", CorrectOptionId = "B",
                    Options = new() { new() { Id = "A", Text = "1" }, new() { Id = "B", Text = "2" } },
                }).ToList(),
                Exercises = new() { new() { Statement = "S", Difficulty = "extrême" } },
            };
            var s = TutorSessionMaterialService.Sanitize(c);
            Assert.Equal(200, s.Title.Length);
            Assert.Equal(TutorSessionMaterialService.MaxQuiz, s.Quiz.Count);
            Assert.Equal("q15", s.Quiz.Last().Id);
            Assert.All(s.Quiz, q => Assert.Equal("b", q.CorrectOptionId));
            Assert.Equal("moyen", s.Exercises.Single().Difficulty);
        }
    }

    /// <summary>
    /// Lot 9.6 : script manuel SQL_TutorSessionMaterials.sql sur un vrai
    /// PostgreSQL jetable (rejoué par la fixture) : tables et index réels,
    /// droit posé une seule fois sur les plans professeur, un réglage admin
    /// ultérieur n'est pas écrasé par un nouveau passage.
    /// </summary>
    public class TutorSessionMaterialPostgresTests : IClassFixture<PostgresTestDatabase>
    {
        private readonly PostgresTestDatabase _pg;
        public TutorSessionMaterialPostgresTests(PostgresTestDatabase pg) => _pg = pg;

        [PostgresFact]
        public async Task Script_IsIdempotent_AndKeepsAdminChoices()
        {
            await using var db = _pg.CreateContext();
            var script = await File.ReadAllTextAsync(PostgresTestDatabase.FindMigration("SQL_TutorSessionMaterials.sql"));
            var teacherVersions = await db.PlanVersions.AsNoTracking()
                .Where(v => v.PricingPlan!.Audience == "teacher").Select(v => v.Id).ToListAsync();
            Assert.NotEmpty(teacherVersions);

            // Base déjà migrée avant le 9.6 : grille sans le droit, script jamais passé.
            await db.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""PlanEntitlements"" WHERE ""Key"" = 'ai.feature.session_material';
                DELETE FROM ""__EFMigrationsHistory"" WHERE ""MigrationId"" = '20261018120000_TutorSessionMaterials';");
            await db.Database.ExecuteSqlRawAsync(script);
            await db.Database.ExecuteSqlRawAsync(script);
            foreach (var v in teacherVersions)
                Assert.True(await db.PlanEntitlements.AnyAsync(e => e.PlanVersionId == v && e.Key == EntitlementCatalog.AiSessionMaterial && e.Value == "true"));
            Assert.False(await db.PlanEntitlements.AnyAsync(e => e.Key == EntitlementCatalog.AiSessionMaterial
                && !teacherVersions.Contains(e.PlanVersionId)));

            // Réglage admin : droit fermé sur une version, puis nouveau passage du script.
            var target = await db.PlanEntitlements.FirstAsync(e => e.PlanVersionId == teacherVersions[0] && e.Key == EntitlementCatalog.AiSessionMaterial);
            db.PlanEntitlements.Remove(target);
            await db.SaveChangesAsync();
            await db.Database.ExecuteSqlRawAsync(script);
            Assert.False(await db.PlanEntitlements.AnyAsync(e => e.PlanVersionId == teacherVersions[0] && e.Key == EntitlementCatalog.AiSessionMaterial));

            // Tables réelles : un support par séance, un résultat par élève.
            var student = new User { Email = $"m96-{Guid.NewGuid():N}@pg.local", Role = "student" };
            var tutor = new User { Email = $"m96t-{Guid.NewGuid():N}@pg.local", Role = "teacher" };
            db.Users.AddRange(student, tutor);
            await db.SaveChangesAsync();
            var profile = new TutorProfile { UserId = tutor.Id, IsActive = true, HourlyRateXaf = 5000 };
            db.TutorProfiles.Add(profile);
            await db.SaveChangesAsync();
            Assert.False((await db.TutorProfiles.AsNoTracking().SingleAsync(p => p.Id == profile.Id)).AutoGenerateSessionMaterial);
            var booking = new TutorBooking
            {
                TutorProfileId = profile.Id, StudentUserId = student.Id, SessionDate = DateOnly.FromDateTime(DateTime.UtcNow),
                StartTime = new TimeSpan(9, 0, 0), EndTime = new TimeSpan(10, 0, 0), Status = "confirmed", PaymentStatus = "completed",
            };
            db.TutorBookings.Add(booking);
            await db.SaveChangesAsync();
            var m = new TutorSessionMaterial { TutorBookingId = booking.Id, TutorUserId = tutor.Id, Title = new string('x', 200), ContentJson = "{}" };
            db.TutorSessionMaterials.Add(m);
            await db.SaveChangesAsync();
            db.TutorSessionMaterialResults.Add(new TutorSessionMaterialResult { TutorSessionMaterialId = m.Id, StudentUserId = student.Id });
            await db.SaveChangesAsync();

            await using var db2 = _pg.CreateContext();
            db2.TutorSessionMaterials.Add(new TutorSessionMaterial { TutorBookingId = booking.Id, TutorUserId = tutor.Id, ContentJson = "{}" });
            await Assert.ThrowsAsync<DbUpdateException>(() => db2.SaveChangesAsync());
            await using var db3 = _pg.CreateContext();
            db3.TutorSessionMaterialResults.Add(new TutorSessionMaterialResult { TutorSessionMaterialId = m.Id, StudentUserId = student.Id });
            await Assert.ThrowsAsync<DbUpdateException>(() => db3.SaveChangesAsync());
        }
    }
}
