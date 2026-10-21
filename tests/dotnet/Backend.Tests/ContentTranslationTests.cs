using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Translation;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Contenus de la base servis dans la langue de l'utilisateur : choix de la
    /// langue, repli sur le français, traductions humaines jamais écrasées,
    /// filtres reçus en anglais, cycle de vie à l'enregistrement.
    /// </summary>
    public class ContentTranslationTests
    {
        // ── Outils ─────────────────────────────────────────────────────────

        private sealed class FakeTranslator : IContentTextTranslator
        {
            public bool Fail { get; set; }
            public List<string> Sent { get; } = new();
            public Func<string, string> Map { get; set; } = t => "EN " + t;

            public Task<TranslationBatchResult> TranslateAsync(IReadOnlyCollection<string> texts,
                IReadOnlyCollection<(string Fr, string En)> glossary, CancellationToken ct = default)
            {
                Sent.AddRange(texts);
                if (Fail) return Task.FromResult(new TranslationBatchResult(false, new Dictionary<string, string>(), 0, "WinAI indisponible"));
                return Task.FromResult(new TranslationBatchResult(true,
                    texts.Distinct().ToDictionary(t => t, Map), 10 * texts.Count, null));
            }
        }

        private sealed class DbIndex : IContentTranslationIndex
        {
            private readonly ApplicationDbContext _db;
            public DbIndex(ApplicationDbContext db) => _db = db;
            public Task<ContentTranslationSnapshot> GetAsync(CancellationToken ct = default) => ContentTranslationIndex.LoadAsync(_db, ct);
            public void Invalidate() { }
        }

        private static (ContentTranslationService svc, FakeTranslator tr) Service(ApplicationDbContext db)
        {
            var tr = new FakeTranslator();
            return (new ContentTranslationService(db, tr, new DbIndex(db), NullLogger<ContentTranslationService>.Instance), tr);
        }

        private static Subject PublishedSubject(int id, string title, string? description = null, string? category = "Mathématiques") =>
            new() { Id = id, Title = title, Description = description, Category = category, IsPublished = true };

        // ── Choix de la langue ─────────────────────────────────────────────

        private static HttpContext Context(string? query = null, string? acceptLanguage = null, string? userLocale = null)
        {
            var db = TestApplicationDbContext.Create();
            var services = new ServiceCollection();
            services.AddSingleton<ApplicationDbContext>(db);
            var ctx = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
            if (query != null) ctx.Request.QueryString = new QueryString(query);
            if (acceptLanguage != null) ctx.Request.Headers.AcceptLanguage = acceptLanguage;
            if (userLocale != null)
            {
                db.Users.Add(new User { Id = 7, Email = "u@test.local", Role = "student", Locale = userLocale });
                db.SaveChanges();
                ctx.User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "7") }, "test"));
            }
            return ctx;
        }

        [Fact]
        public async Task Language_QueryParameter_WinsOverAccountAndHeader()
        {
            Assert.Equal("en", await ContentLanguage.ResolveAsync(Context("?lang=en", "fr-FR", "fr")));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context("?lang=fr", "en-GB", "en")));
        }

        [Fact]
        public async Task Language_ConnectedUser_UsesUserLocale()
        {
            Assert.Equal("en", await ContentLanguage.ResolveAsync(Context(null, "fr-FR,fr;q=0.9", "en")));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context(null, "en-US", "fr")));
        }

        [Fact]
        public async Task Language_Anonymous_UsesAcceptLanguage_ThenFrench()
        {
            Assert.Equal("en", await ContentLanguage.ResolveAsync(Context(null, "de-DE,en;q=0.8,fr;q=0.5")));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context(null, "fr-CM,en;q=0.5")));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context(null, "de-DE")));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context()));
            Assert.Equal("fr", await ContentLanguage.ResolveAsync(Context("?lang=xx")));
        }

        // ── Localisation des réponses ──────────────────────────────────────

        private static ContentTranslationSnapshot Snapshot() => ContentTranslationSnapshot.Build(
            new (string, string?)[]
            {
                ("Épreuve de mathématiques BEPC 2023", "BEPC 2023 mathematics paper"),
                ("Sujet complet avec corrigé.", "Full paper with worked solution."),
                ("Livre de physique", null),
                ("algèbre", "algebra"),
            },
            new[]
            {
                new ContentTranslation { Kind = "category", ValueFr = "Mathématiques", ValueEn = "Mathematics", Origin = "human" },
                new ContentTranslation { Kind = "category", ValueFr = "Physique", ValueEn = "Physics", Origin = "human" },
                new ContentTranslation { Kind = "examType", ValueFr = "Baccalauréat", ValueEn = "Baccalaureate", Origin = "human" },
                new ContentTranslation { Kind = "level", ValueFr = "Toutes séries", ValueEn = "All series", Origin = "auto", NeedsReview = true },
            });

        [Fact]
        public void Localize_ReplacesContentAndTaxonomy_KeepsFieldNames_AndMarksEnglish()
        {
            var node = JsonNode.Parse("""
                {"data":[{"id":1,"title":"Épreuve de mathématiques BEPC 2023","description":"Sujet complet avec corrigé.",
                  "category":"mathématiques","examType":"Baccalauréat","level":"Toutes séries","tags":["algèbre"],
                  "value":"Mathématiques","slug":"Physique"}]}
                """)!;
            ContentLocalizationFilter.Localize(node, Snapshot());
            var item = node["data"]![0]!;
            Assert.Equal("BEPC 2023 mathematics paper", (string)item["title"]!);
            Assert.Equal("Full paper with worked solution.", (string)item["description"]!);
            Assert.Equal("Mathematics", (string)item["category"]!);
            Assert.Equal("mathématiques", (string)item["categoryFr"]!);
            Assert.Equal("Baccalauréat", (string)item["examTypeFr"]!);
            Assert.Equal("Baccalaureate", (string)item["examType"]!);
            Assert.Equal("All series", (string)item["level"]!);
            Assert.Equal("algebra", (string)item["tags"]![0]!);
            // Valeurs techniques jamais remplacées (filtres, URL).
            Assert.Equal("Mathématiques", (string)item["value"]!);
            Assert.Equal("Physique", (string)item["slug"]!);
            Assert.Equal("en", (string)item["contentLang"]!);
        }

        [Fact]
        public void Localize_FallsBackToFrench_AndMarksContentLangFr()
        {
            var node = JsonNode.Parse("""{"id":2,"title":"Livre de physique","category":"Physique"}""")!;
            ContentLocalizationFilter.Localize(node, Snapshot());
            Assert.Equal("Livre de physique", (string)node["title"]!);
            Assert.Equal("Physics", (string)node["category"]!);
            Assert.Equal("fr", (string)node["contentLang"]!);
        }

        [Fact]
        public void Localize_LeavesUnknownTextAlone_WithoutContentLang()
        {
            var node = JsonNode.Parse("""{"title":"Message du forum","author":"Awa"}""")!;
            ContentLocalizationFilter.Localize(node, Snapshot());
            Assert.Equal("Message du forum", (string)node["title"]!);
            Assert.Null(node["contentLang"]);
        }

        [Fact]
        public void EditingAndAdminRoutes_AreNeverLocalized()
        {
            Assert.False(ContentLocalizationFilter.IsLocalizablePath("/api/admin/exams"));
            Assert.False(ContentLocalizationFilter.IsLocalizablePath("/api/teacher/courses/3"));
            Assert.False(ContentLocalizationFilter.IsLocalizablePath("/api/content-translations/subject/3"));
            Assert.True(ContentLocalizationFilter.IsLocalizablePath("/api/teacher/bank"));
            Assert.True(ContentLocalizationFilter.IsLocalizablePath("/api/subjects"));
            Assert.True(ContentLocalizationFilter.IsLocalizablePath("/api/courses/4"));
        }

        // ── Traductions humaines jamais écrasées ───────────────────────────

        [Fact]
        public async Task Human_IsNeverOverwritten_AndBecomesReviewWhenFrenchChanges()
        {
            using var db = TestApplicationDbContext.Create();
            var s = PublishedSubject(1, "Physique BEPC 2022", "Énoncé.");
            s.TitleEn = "Physics BEPC 2022 (checked)";
            s.TranslationOrigin = TranslationOrigins.Human;
            s.TranslationState = TranslationStates.Ok;
            s.TranslationSourceHash = ContentTranslationService.SourceHash(s);
            db.Subjects.Add(s);
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);

            s.Title = "Physique BEPC 2022, sujet officiel";
            Assert.False(ContentTranslationService.MarkSourceChanged(s));
            Assert.Equal(TranslationStates.Review, s.TranslationState);

            var outcome = await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal("Physics BEPC 2022 (checked)", db.Subjects.Single().TitleEn);
            Assert.Equal(TranslationOrigins.Human, db.Subjects.Single().TranslationOrigin);
            Assert.Equal(TranslationStates.Review, outcome.State);
            // Seule la matière (taxonomie inconnue ici) part en traduction, jamais le titre.
            Assert.DoesNotContain(tr.Sent, t => t.StartsWith("Physique"));
        }

        [Fact]
        public async Task Retranslate_OnHuman_ReturnsSuggestionWithoutSaving()
        {
            using var db = TestApplicationDbContext.Create();
            var s = PublishedSubject(1, "Chimie 2021");
            s.TitleEn = "Chemistry 2021 (manual)";
            s.TranslationOrigin = TranslationOrigins.Human;
            s.TranslationSourceHash = ContentTranslationService.SourceHash(s);
            s.TranslationState = TranslationStates.Ok;
            db.Subjects.Add(s);
            await db.SaveChangesAsync();
            var (svc, _) = Service(db);

            var outcome = await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1), force: true, onlyPublished: false);
            Assert.Equal("Chemistry 2021 (manual)", db.Subjects.Single().TitleEn);
            Assert.Equal("EN Chimie 2021", outcome.Suggestion!["titleEn"]);
        }

        [Fact]
        public async Task SaveHuman_MarksHuman_AndAutoNoLongerTouchesIt()
        {
            using var db = TestApplicationDbContext.Create();
            db.Subjects.Add(PublishedSubject(1, "Histoire 2020", "Sujet d'histoire."));
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal(TranslationOrigins.Auto, db.Subjects.Single().TranslationOrigin);

            await svc.SaveHumanAsync(new ContentRef(ContentKinds.Subject, 1), new JsonObject { ["titleEn"] = "History 2020" });
            var row = db.Subjects.Single();
            Assert.Equal("History 2020", row.TitleEn);
            Assert.Equal("EN Sujet d'histoire.", row.DescriptionEn);
            Assert.Equal(TranslationOrigins.Human, row.TranslationOrigin);

            tr.Sent.Clear();
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1), force: true);
            Assert.Equal("History 2020", db.Subjects.Single().TitleEn);
        }

        // ── Traduction automatique ─────────────────────────────────────────

        [Fact]
        public async Task Auto_IsRedoneWhenFrenchChanges()
        {
            using var db = TestApplicationDbContext.Create();
            db.Subjects.Add(PublishedSubject(1, "Physique 2019"));
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal("EN Physique 2019", db.Subjects.Single().TitleEn);

            // Rien n'a changé : aucun nouvel appel à WinAI.
            tr.Sent.Clear();
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Empty(tr.Sent);

            var s = db.Subjects.Single();
            s.Title = "Physique 2019, session de juin";
            Assert.True(ContentTranslationService.MarkSourceChanged(s));
            await db.SaveChangesAsync();
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal("EN Physique 2019, session de juin", db.Subjects.Single().TitleEn);
            Assert.Equal(TranslationStates.Ok, db.Subjects.Single().TranslationState);
        }

        [Fact]
        public async Task Failure_DoesNotThrow_KeepsFrench_AndIsRetriedLater()
        {
            using var db = TestApplicationDbContext.Create();
            db.Subjects.Add(PublishedSubject(1, "Chimie BEPC"));
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);
            tr.Fail = true;

            var outcome = await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal(TranslationStates.Failed, outcome.State);
            Assert.Null(db.Subjects.Single().TitleEn);

            // La reprise planifiée retrouve les contenus en échec (pas ceux jamais demandés).
            db.Subjects.Add(PublishedSubject(2, "Jamais demandé"));
            await db.SaveChangesAsync();
            var (retry, _) = await svc.FindMissingAsync(includeNeverRequested: false);
            Assert.Equal(new[] { 1 }, retry.Where(r => r.Kind == ContentKinds.Subject).Select(r => r.Id).ToArray());
            var (initial, chars) = await svc.FindMissingAsync(includeNeverRequested: true);
            Assert.Equal(2, initial.Count(r => r.Kind == ContentKinds.Subject));
            Assert.True(chars > 0);

            tr.Fail = false;
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Equal("EN Chimie BEPC", db.Subjects.Single(s => s.Id == 1).TitleEn);
        }

        [Fact]
        public async Task Drafts_AreNotTranslatedByBackgroundTask()
        {
            using var db = TestApplicationDbContext.Create();
            var s = PublishedSubject(1, "Brouillon");
            s.IsPublished = false;
            db.Subjects.Add(s);
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            Assert.Empty(tr.Sent);
            Assert.Null(db.Subjects.Single().TitleEn);
        }

        [Fact]
        public async Task Course_TranslatesSectionsLessonsAndLists()
        {
            using var db = TestApplicationDbContext.Create();
            db.Courses.Add(new Course
            {
                Id = 5, Title = "Réussir le BAC C", Slug = "bac-c", Status = "published", Category = "Mathématiques",
                Objectives = new List<string> { "Maîtriser les dérivées" }, Tags = new List<string> { "bac" },
            });
            db.CourseSections.Add(new CourseSection { Id = 50, CourseId = 5, Title = "Chapitre 1 : fonctions" });
            db.CourseLessons.Add(new CourseLesson { Id = 500, CourseId = 5, SectionId = 50, Title = "Leçon 1" });
            await db.SaveChangesAsync();
            var (svc, _) = Service(db);

            await svc.TranslateAsync(new ContentRef(ContentKinds.Course, 5));
            var c = db.Courses.Single();
            Assert.Equal("EN Réussir le BAC C", c.TitleEn);
            Assert.Equal(new[] { "EN Maîtriser les dérivées" }, c.ObjectivesEn);
            Assert.Equal("EN Chapitre 1 : fonctions", db.CourseSections.Single().TitleEn);
            Assert.Equal("EN Leçon 1", db.CourseLessons.Single().TitleEn);
        }

        [Fact]
        public async Task NewTaxonomyValue_IsAddedAsAutoToReview()
        {
            using var db = TestApplicationDbContext.Create();
            db.ContentTranslations.Add(new ContentTranslation { Kind = "category", ValueFr = "Mathématiques", ValueEn = "Mathematics" });
            db.Subjects.Add(PublishedSubject(1, "Géologie L1", category: "Géologie"));
            db.Subjects.Add(PublishedSubject(2, "Maths", category: "mathématiques"));
            await db.SaveChangesAsync();
            var (svc, tr) = Service(db);
            tr.Map = t => t == "Géologie" ? "Geology" : "EN " + t;

            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 1));
            await svc.TranslateAsync(new ContentRef(ContentKinds.Subject, 2));
            var added = db.ContentTranslations.Single(t => t.ValueFr == "Géologie");
            Assert.Equal("Geology", added.ValueEn);
            Assert.Equal(TranslationOrigins.Auto, added.Origin);
            Assert.True(added.NeedsReview);
            // « mathématiques » est déjà connue (casse ignorée) : pas de doublon.
            Assert.Equal(2, db.ContentTranslations.Count());
        }

        // ── Filtres en anglais ─────────────────────────────────────────────

        [Fact]
        public async Task ExamFilters_SentInEnglish_FindFrenchValues()
        {
            using var db = TestApplicationDbContext.Create();
            db.ContentTranslations.Add(new ContentTranslation { Kind = "category", ValueFr = "Mathématiques", ValueEn = "Mathematics" });
            db.ContentTranslations.Add(new ContentTranslation { Kind = "examType", ValueFr = "Baccalauréat", ValueEn = "Baccalaureate" });
            db.Exams.Add(new Exam { Id = 1, Title = "Maths BAC 2023", TitleEn = "BAC 2023 maths paper", ExamType = "Baccalauréat", Category = "Mathématiques", Year = 2023 });
            db.Exams.Add(new Exam { Id = 2, Title = "Physique BAC 2023", ExamType = "Baccalauréat", Category = "Physique", Year = 2023 });
            await db.SaveChangesAsync();
            var service = new ExamService(db, new DbIndex(db));

            var (byCategory, _) = await service.SearchExamsAsync(new ExamSearchFilterDto { Category = "Mathematics" });
            Assert.Equal(new[] { 1 }, byCategory.Select(e => e.Id).ToArray());
            var (byType, total) = await service.SearchExamsAsync(new ExamSearchFilterDto { ExamType = "baccalaureate" });
            Assert.Equal(2, total);
            var (french, _) = await service.SearchExamsAsync(new ExamSearchFilterDto { Category = "Mathématiques" });
            Assert.Single(french);
            // Recherche libre sur le titre anglais.
            var (search, _) = await service.SearchExamsAsync(new ExamSearchFilterDto { SearchTerm = "maths paper" });
            Assert.Equal(new[] { 1 }, search.Select(e => e.Id).ToArray());
        }

        [Fact]
        public void FrenchEquivalents_IncludeValueAndFrenchSources()
        {
            var eq = Snapshot().FrenchEquivalents("Mathematics");
            Assert.Contains("mathematics", eq);
            Assert.Contains("mathématiques", eq);
            Assert.Equal(new[] { "inconnu" }, Snapshot().FrenchEquivalents("Inconnu"));
        }

        // ── Cycle de vie à l'enregistrement ────────────────────────────────

        [Fact]
        public async Task SaveInterceptor_MarksPending_AndQueues_HumanBecomesReview()
        {
            var queue = new ContentTranslationQueue();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"interceptor-{Guid.NewGuid()}")
                .AddInterceptors(new ContentTranslationSaveInterceptor(queue))
                .Options;
            using var db = new TestApplicationDbContext(options);

            db.Subjects.Add(PublishedSubject(1, "Nouveau livre"));
            await db.SaveChangesAsync();
            Assert.Equal(TranslationStates.Pending, db.Subjects.Single().TranslationState);
            Assert.True(queue.TryDequeue(out var item));
            Assert.Equal(new ContentRef(ContentKinds.Subject, 1), item);

            // Écriture de la traduction elle-même : pas de nouvelle mise en file.
            var s = db.Subjects.Single();
            s.TitleEn = "New book";
            s.TranslationOrigin = TranslationOrigins.Human;
            s.TranslationState = TranslationStates.Ok;
            s.TranslationSourceHash = ContentTranslationService.SourceHash(s);
            await db.SaveChangesAsync();
            Assert.False(queue.TryDequeue(out _));

            s.Title = "Nouveau livre, 2e édition";
            await db.SaveChangesAsync();
            Assert.Equal(TranslationStates.Review, db.Subjects.Single().TranslationState);
            Assert.Equal("New book", db.Subjects.Single().TitleEn);
        }

        [Fact]
        public async Task Concours_FillsEmptyEnglish_NeverOverwritesManualEnglish()
        {
            using var db = TestApplicationDbContext.Create();
            var ev = new ConcoursEvent { Id = 3, Slug = "ens", Year = 2026, Name = "Concours ENS", Notes = "Inscriptions en ligne.", NotesEn = "Online registration (manual)." };
            db.ConcoursEvents.Add(ev);
            await db.SaveChangesAsync();
            var (svc, _) = Service(db);

            await svc.TranslateConcoursEventAsync(3);
            var saved = db.ConcoursEvents.Single();
            Assert.Equal("EN Concours ENS", saved.NameEn);
            Assert.Equal("Online registration (manual).", saved.NotesEn);

            // Le français des notes change : la version manuelle est gardée et signalée.
            saved.Notes = "Inscriptions en ligne jusqu'au 15 mars.";
            saved.Name = "Concours d'entrée ENS";
            await db.SaveChangesAsync();
            var outcome = await svc.TranslateConcoursEventAsync(3);
            saved = db.ConcoursEvents.Single();
            Assert.Equal("Online registration (manual).", saved.NotesEn);
            Assert.Equal("EN Concours d'entrée ENS", saved.NameEn);
            Assert.Equal(TranslationStates.Review, outcome.State);
            Assert.Contains("NotesEn", ConcoursTranslationMeta.Parse(saved.TranslationMetaJson).Review);
        }

        [Fact]
        public void Concours_AdminWrite_ManualEditBecomesHuman_ButtonFillStaysAuto()
        {
            var ev = new ConcoursEvent { Name = "Concours IUT", Notes = "Dossier complet." };
            var before = ConcoursTranslationMeta.Snapshot(ev);
            ev.NameEn = "IUT entrance exam";
            ev.NotesEn = "Complete file (typed)";
            ConcoursTranslationMeta.ApplyAdminWrite(ev, before, new[] { "NameEn" });
            var meta = ConcoursTranslationMeta.Parse(ev.TranslationMetaJson);
            Assert.True(meta.Auto.ContainsKey("NameEn"));
            Assert.True(meta.Human.ContainsKey("NotesEn"));
        }

        [Fact]
        public void TokenEstimate_IsReasonable()
        {
            // ~170 épreuves et livres d'environ 500 caractères.
            var tokens = ContentTranslationService.EstimateTokens(85_000, 170);
            Assert.InRange(tokens, 120_000, 250_000);
        }
    }
}
