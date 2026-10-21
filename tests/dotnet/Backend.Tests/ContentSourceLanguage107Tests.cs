using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Backend.Data;
using Backend.Models.Entities;
using Backend.Services.Translation;
using Backend.Utils;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.7 : épreuves écrites en anglais d'origine (GCE). Langue déduite au
    /// dépôt, jamais envoyée à la traduction, marquée « en » par l'API, et
    /// regroupées sous les pages GCE O Level / A Level.
    /// </summary>
    public class ContentSourceLanguage107Tests
    {
        private sealed class RecordingTranslator : IContentTextTranslator
        {
            public List<string> Sent { get; } = new();

            public Task<TranslationBatchResult> TranslateAsync(IReadOnlyCollection<string> texts,
                IReadOnlyCollection<(string Fr, string En)> glossary, CancellationToken ct = default)
            {
                Sent.AddRange(texts);
                return Task.FromResult(new TranslationBatchResult(true,
                    texts.Distinct().ToDictionary(t => t, t => "EN " + t), texts.Count, null));
            }
        }

        private sealed class DbIndex : IContentTranslationIndex
        {
            private readonly ApplicationDbContext _db;
            public DbIndex(ApplicationDbContext db) => _db = db;
            public Task<ContentTranslationSnapshot> GetAsync(CancellationToken ct = default) => ContentTranslationIndex.LoadAsync(_db, ct);
            public void Invalidate() { }
        }

        private static ApplicationDbContext DbWithInterceptor(ContentTranslationQueue queue)
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"source-language-{Guid.NewGuid()}")
                .AddInterceptors(new ContentTranslationSaveInterceptor(queue))
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.CoreEventId.ManyServiceProvidersCreatedWarning))
                .Options;
            return new TestApplicationDbContext(options);
        }

        [Theory]
        [InlineData("GCE Ordinary Level", "en")]
        [InlineData("Physics: GCE O Level", "en")]
        [InlineData("Upper Sixth (GCE A Level)", "en")]
        [InlineData("Form 5", "en")]
        [InlineData("Lower Sixth", "en")]
        [InlineData("Class 6", "en")]
        [InlineData("First School Leaving Certificate (FSLC)", "en")]
        [InlineData("Constitutional Law: University Year 2", "en")]
        [InlineData("Research Methods: Master's Degree", "en")]
        [InlineData("Baccalauréat C", "fr")]
        [InlineData("Classe de 3e", "fr")]
        [InlineData("Épreuve de mathématiques BEPC 2023", "fr")]
        [InlineData("Formation médicale – QCM", "fr")]
        [InlineData("Niveau 3", "fr")]
        public void Infer_RecognisesAnglophoneLabelsOnly(string label, string expected)
        {
            Assert.Equal(expected, ContentSourceLanguage.Infer(label));
        }

        [Theory]
        [InlineData("GCE O Level", "gce-o-level", true)]
        [InlineData("GCE Ordinary Level", "gce-o-level", true)]
        [InlineData("GCE O/L – Mathematics", "gce-o-level", true)]
        [InlineData("GCE_O", "gce-o-level", true)]
        [InlineData("GCE Advanced Level", "gce-a-level", true)]
        [InlineData("GCE A Level / Paper 2", "gce-a-level", true)]
        [InlineData("GCE Advanced Level", "gce-o-level", false)]
        [InlineData("GCE Ordinary Level", "gce-a-level", false)]
        [InlineData("Baccalauréat", "gce-a-level", false)]
        public void ExamTypeGroups_GroupsGcePapers(string examType, string requested, bool expected)
        {
            Assert.Equal(expected, ExamTypeGroups.Matches(examType, requested));
        }

        [Fact]
        public async Task NewGcePaper_IsMarkedEnglish_AndNeverSentToTranslation()
        {
            var queue = new ContentTranslationQueue();
            using var db = DbWithInterceptor(queue);
            db.Exams.Add(new Exam { Id = 1, Title = "Chemistry Paper 1", ExamType = "GCE Ordinary Level", Category = "Chemistry", Year = 2024, IsPublished = true });
            db.Subjects.Add(new Subject { Id = 2, Title = "Épreuve de chimie", Category = "Chimie", IsPublished = true });
            await db.SaveChangesAsync();

            var exam = db.Exams.Single();
            Assert.Equal("en", exam.Language);
            Assert.Equal(TranslationOrigins.Source, exam.TranslationOrigin);
            Assert.Equal(TranslationStates.Ok, exam.TranslationState);
            Assert.Equal("fr", db.Subjects.Single().Language);
            Assert.Equal(TranslationStates.Pending, db.Subjects.Single().TranslationState);

            var tr = new RecordingTranslator();
            var svc = new ContentTranslationService(db, tr, new DbIndex(db), NullLogger<ContentTranslationService>.Instance);
            var outcome = await svc.TranslateAsync(new ContentRef(ContentKinds.Exam, 1));
            Assert.Equal(TranslationStates.Ok, outcome.State);
            Assert.Empty(tr.Sent);
            Assert.Null(db.Exams.Single().TitleEn);

            var (missing, _) = await svc.FindMissingAsync(includeNeverRequested: true);
            Assert.DoesNotContain(missing, r => r.Kind == ContentKinds.Exam);
            Assert.Contains(missing, r => r.Kind == ContentKinds.Subject && r.Id == 2);
        }

        [Fact]
        public async Task ExplicitLanguage_WinsOverInference_AndSwitchingBackToFrenchQueuesTranslation()
        {
            var queue = new ContentTranslationQueue();
            using var db = DbWithInterceptor(queue);
            db.Subjects.Add(new Subject { Id = 1, Title = "Anglais : compréhension écrite", Category = "Anglais", IsPublished = true, Language = "EN" });
            await db.SaveChangesAsync();
            Assert.Equal("en", db.Subjects.Single().Language);
            Assert.Equal(TranslationOrigins.Source, db.Subjects.Single().TranslationOrigin);

            var s = db.Subjects.Single();
            s.Language = "fr";
            await db.SaveChangesAsync();
            Assert.Null(db.Subjects.Single().TranslationOrigin);
            Assert.Equal(TranslationStates.Pending, db.Subjects.Single().TranslationState);
        }

        [Fact]
        public async Task Localize_EnglishPaper_IsLeftAsIs_AndMarkedEnglish()
        {
            using var db = TestApplicationDbContext.Create();
            db.Subjects.Add(new Subject { Id = 1, Title = "Physics: GCE O Level", Description = "Mechanics and waves.", Category = "Physics", IsPublished = true, Language = "en" });
            db.Subjects.Add(new Subject { Id = 2, Title = "Livre de physique", Category = "Physique", IsPublished = true });
            await db.SaveChangesAsync();
            var snapshot = await ContentTranslationIndex.LoadAsync(db);

            var node = JsonNode.Parse("""
                [{"id":1,"title":"Physics: GCE O Level","description":"Mechanics and waves.","language":"en"},
                 {"id":2,"title":"Livre de physique","language":"fr"}]
                """)!;
            ContentLocalizationFilter.Localize(node, snapshot);

            Assert.Equal("Physics: GCE O Level", (string)node[0]!["title"]!);
            Assert.Equal("Mechanics and waves.", (string)node[0]!["description"]!);
            Assert.Equal("en", (string)node[0]!["contentLang"]!);
            // Le français non traduit reste signalé comme tel.
            Assert.Equal("fr", (string)node[1]!["contentLang"]!);
        }
    }
}
