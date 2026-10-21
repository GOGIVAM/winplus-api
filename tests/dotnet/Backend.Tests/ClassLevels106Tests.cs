using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services.Translation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.6 : en anglais, les classes francophones s'affichent sous leur
    /// équivalent anglophone (Terminale → Upper Sixth) ; les valeurs
    /// enregistrées restent françaises et les filtres reçus en anglais les
    /// retrouvent.
    /// </summary>
    public class ClassLevels106Tests
    {
        private static ContentTranslationSnapshot Snapshot() => ContentTranslationSnapshot.Build(
            System.Array.Empty<(string, string?)>(),
            new[]
            {
                new ContentTranslation { Kind = "level", ValueFr = "Terminale", ValueEn = "Upper Sixth", Origin = "human" },
                new ContentTranslation { Kind = "level", ValueFr = "Tle", ValueEn = "Upper Sixth", Origin = "human" },
                new ContentTranslation { Kind = "level", ValueFr = "Première", ValueEn = "Lower Sixth", Origin = "human" },
                new ContentTranslation { Kind = "level", ValueFr = "6ème", ValueEn = "Form 1", Origin = "human" },
                new ContentTranslation { Kind = "level", ValueFr = "CM2", ValueEn = "Class 6", Origin = "human" },
            });

        private sealed class FixedIndex : IContentTranslationIndex
        {
            public Task<ContentTranslationSnapshot> GetAsync(CancellationToken ct = default) => Task.FromResult(Snapshot());
            public void Invalidate() { }
        }

        [Fact]
        public void Localize_ShowsAnglophoneEquivalent_AndKeepsFrenchValue()
        {
            var node = JsonNode.Parse("""
                {"items":[{"level":"Terminale","group":"Tle C"}],"filters":{"levels":["6ème","CM2","Première"]}}
                """)!;
            ContentLocalizationFilter.Localize(node, Snapshot());
            var item = node["items"]![0]!;
            Assert.Equal("Upper Sixth", (string)item["level"]!);
            Assert.Equal("Terminale", (string)item["levelFr"]!);
            // Nom de groupe saisi par l'établissement : jamais remplacé.
            Assert.Equal("Tle C", (string)item["group"]!);
            var levels = node["filters"]!["levels"]!.AsArray().Select(v => (string)v!).ToArray();
            Assert.Equal(new[] { "Form 1", "Class 6", "Lower Sixth" }, levels);
        }

        [Fact]
        public void FrenchEquivalents_FindsEveryFrenchSpelling()
        {
            var eq = Snapshot().FrenchEquivalents("Upper Sixth");
            Assert.Contains("terminale", eq);
            Assert.Contains("tle", eq);
        }

        [Fact]
        public async Task InstitutionDirectory_FilterReceivedInEnglish_FindsFrenchLevel()
        {
            using var db = TestApplicationDbContext.Create();
            db.Users.Add(new User { Id = 1, Email = "admin@test.local", Role = "admin" });
            db.Users.Add(new User { Id = 2, Email = "a@test.local", Role = "student", FirstName = "Awa" });
            db.Users.Add(new User { Id = 3, Email = "b@test.local", Role = "student", FirstName = "Ben" });
            db.InstitutionStudents.Add(new InstitutionStudent { Id = 1, InstitutionId = 10, StudentId = 2, Level = "Terminale" });
            db.InstitutionStudents.Add(new InstitutionStudent { Id = 2, InstitutionId = 10, StudentId = 3, Level = "Première" });
            db.SaveChanges();

            var services = new ServiceCollection().AddSingleton<IContentTranslationIndex>(new FixedIndex()).BuildServiceProvider();
            var ctl = new InstitutionStudentsController(db, NullLogger<InstitutionStudentsController>.Instance,
                services.GetRequiredService<IServiceScopeFactory>())
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        RequestServices = services,
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", "1"), new Claim("role", "admin") }, "test")),
                    },
                },
            };

            foreach (var level in new[] { "Upper Sixth", "Terminale" })
            {
                var res = Assert.IsType<OkObjectResult>(await ctl.GetStudents(10, null, level, null, null));
                var json = JsonSerializer.SerializeToNode(res.Value)!;
                var only = Assert.Single(json["data"]!.AsArray());
                Assert.Equal("Terminale", (string)only!["level"]!);
            }
        }
    }
}
