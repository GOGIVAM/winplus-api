using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Utils;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.4 : statistiques publiques par examen (pages concours). Chiffres
    /// issus de la base seulement, types saisis regroupés par école, aucune
    /// épreuve non publiée ni sans fiche catalogue publiée.
    /// </summary>
    public class ExamStats104Tests
    {
        private static JsonElement Data(IActionResult result)
        {
            var ok = Assert.IsType<OkObjectResult>(result);
            var json = JsonSerializer.Serialize(ok.Value, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            return JsonDocument.Parse(json).RootElement.GetProperty("data");
        }

        private static ExamStatsController Controller(TestApplicationDbContext db, IMemoryCache? cache = null) =>
            new(db, cache ?? new MemoryCache(new MemoryCacheOptions()), NullLogger<ExamStatsController>.Instance);

        private static async Task<TestApplicationDbContext> SeedAsync()
        {
            var db = TestApplicationDbContext.Create();
            db.Subjects.AddRange(
                new Subject { Id = 1, Title = "IUT maths 2023", Price = 250, IsPublished = true },
                new Subject { Id = 2, Title = "IUT spécialité 2021", Price = 0, IsPublished = true },
                new Subject { Id = 3, Title = "IUT brouillon", Price = 0, IsPublished = false },
                new Subject { Id = 4, Title = "FMSB chimie", Price = 250, IsPublished = true },
                new Subject { Id = 5, Title = "Médecine physique", Price = 250, IsPublished = true },
                new Subject { Id = 6, Title = "FASA chimie", Price = 0, IsPublished = true },
                new Subject { Id = 7, Title = "IUT supprimée", Price = 0, IsPublished = true, IsDeleted = true });
            var t0 = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
            db.Exams.AddRange(
                new Exam { Id = 10, SubjectId = 1, Title = "IUT maths 2023", ExamType = "IUT/Concours d'entrée – épreuve de Mathématique(QCM)", Category = "Mathématiques", Year = 2023, CorrectionUrl = "c.pdf", CreatedAt = t0.AddDays(2) },
                new Exam { Id = 11, SubjectId = 2, Title = "IUT spécialité 2021", ExamType = "IUT- Épreuve de concours", Category = "Spécialité ", Year = 2021, CreatedAt = t0.AddDays(5) },
                new Exam { Id = 12, SubjectId = 3, Title = "IUT brouillon", ExamType = "IUT", Category = "Mathématiques", Year = 2020, CreatedAt = t0 },
                new Exam { Id = 13, SubjectId = 1, Title = "IUT non publiée", ExamType = "IUT", Category = "Mathématiques", Year = 2019, IsPublished = false, CreatedAt = t0 },
                new Exam { Id = 14, SubjectId = 4, Title = "FMSB chimie", ExamType = "FMSB/Examen national d'aptitude", Category = "Chimie", Year = 2019, CreatedAt = t0 },
                new Exam { Id = 15, SubjectId = 5, Title = "Médecine physique", ExamType = "QCM – Examen national d’aptitude à la formation médicale", Category = "Physique", Year = 2020, CreatedAt = t0 },
                new Exam { Id = 16, SubjectId = 6, Title = "FASA chimie", ExamType = "FASA", Category = "Chimie", Year = 2020, CreatedAt = t0 },
                new Exam { Id = 17, SubjectId = 7, Title = "IUT supprimée", ExamType = "IUT", Category = "Mathématiques", Year = 2018, CreatedAt = t0 },
                new Exam { Id = 18, SubjectId = null, Title = "IUT sans fiche", ExamType = "IUT", Category = "Mathématiques", Year = 2018, CreatedAt = t0 });
            await db.SaveChangesAsync();
            return db;
        }

        [Fact]
        public async Task Iut_GroupsFreeTextTypes_AndCountsOnlyPublishedExamsWithPublishedSubject()
        {
            using var db = await SeedAsync();
            var data = Data(await Controller(db).Get("IUT"));

            Assert.Equal("iut", data.GetProperty("examType").GetString());
            Assert.Equal(2, data.GetProperty("totalExams").GetInt32());
            Assert.Equal(1, data.GetProperty("withCorrection").GetInt32());
            Assert.Equal(1, data.GetProperty("freeExams").GetInt32());
            Assert.Equal(new[] { 2023, 2021 }, data.GetProperty("years").EnumerateArray().Select(y => y.GetInt32()).ToArray());
            // Plus récentes d'abord ; le lien public passe par la fiche catalogue.
            var exams = data.GetProperty("exams").EnumerateArray().ToList();
            Assert.Equal(new[] { 11, 10 }, exams.Select(e => e.GetProperty("id").GetInt32()).ToArray());
            Assert.Equal(new[] { 2, 1 }, exams.Select(e => e.GetProperty("subjectId").GetInt32()).ToArray());
            // Catégorie nettoyée des espaces saisis.
            var cats = data.GetProperty("categories").EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToList();
            Assert.Contains("Spécialité", cats);
            Assert.Contains("Mathématiques", cats);
        }

        [Fact]
        public async Task Fmsb_IncludesNationalMedicalAptitudeExam_WithoutFmsbPrefix()
        {
            using var db = await SeedAsync();
            var data = Data(await Controller(db).Get("fmsb"));
            var ids = data.GetProperty("exams").EnumerateArray().Select(e => e.GetProperty("id").GetInt32()).OrderBy(i => i).ToArray();
            Assert.Equal(new[] { 14, 15 }, ids);
        }

        [Fact]
        public async Task UnknownType_ReturnsZeros_NotAnError()
        {
            using var db = await SeedAsync();
            var data = Data(await Controller(db).Get("ens"));
            Assert.Equal(0, data.GetProperty("totalExams").GetInt32());
            Assert.Equal(0, data.GetProperty("exams").GetArrayLength());
            Assert.Equal(0, data.GetProperty("years").GetArrayLength());
        }

        [Fact]
        public async Task InvalidType_IsRejected()
        {
            using var db = await SeedAsync();
            Assert.IsType<BadRequestObjectResult>(await Controller(db).Get("   "));
            Assert.IsType<BadRequestObjectResult>(await Controller(db).Get(new string('a', 80)));
        }

        [Fact]
        public async Task Result_IsCached_UntilExpiry()
        {
            using var db = await SeedAsync();
            var cache = new MemoryCache(new MemoryCacheOptions());
            Assert.Equal(1, Data(await Controller(db, cache).Get("fasa")).GetProperty("totalExams").GetInt32());

            db.Subjects.Add(new Subject { Id = 8, Title = "FASA maths", Price = 0, IsPublished = true });
            db.Exams.Add(new Exam { Id = 19, SubjectId = 8, Title = "FASA maths", ExamType = "FASA", Category = "Mathématiques", Year = 2024 });
            await db.SaveChangesAsync();

            Assert.Equal(1, Data(await Controller(db, cache).Get("fasa")).GetProperty("totalExams").GetInt32());
            Assert.Equal(2, Data(await Controller(db).Get("fasa")).GetProperty("totalExams").GetInt32());
        }

        [Theory]
        [InlineData("Épreuve de Français – Probatoire séries F, AF, CI et BT", "probatoire", true)]
        [InlineData("Probatoire de Technicien – épreuve écrite", "probatoire", true)]
        [InlineData("Examen national d'entrée en 1ère année de formation médicale – QCM", "fmsb", true)]
        [InlineData("QCM – concours d’entrée", "fmsb", false)]
        [InlineData("Concours d'entrée / épreuve de spécialité", "polytechnique", false)]
        [InlineData("FASA", "fmsb", false)]
        public void Fragments_MatchOnlyTheIntendedExam(string examType, string requested, bool expected)
        {
            Assert.Equal(expected, ExamTypeGroups.Matches(examType, requested));
        }
    }
}
