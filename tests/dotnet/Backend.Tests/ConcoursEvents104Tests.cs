using System;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests
{
    /// <summary>
    /// Lot 10.4 : écran admin « Contenus des concours ». CRUD réservé à
    /// l'administrateur, source obligatoire pour toute information officielle,
    /// publication et dépublication, champs éditoriaux et versions anglaises
    /// visibles sur l'API publique.
    /// </summary>
    public class ConcoursEvents104Tests
    {
        private static readonly JsonSerializerOptions Camel = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        private static JsonElement Body(IActionResult result)
        {
            var value = result switch
            {
                ObjectResult o => o.Value,
                _ => throw new InvalidOperationException(result.GetType().Name),
            };
            return JsonDocument.Parse(JsonSerializer.Serialize(value, Camel)).RootElement;
        }

        private static AdminConcoursEventsController Admin(TestApplicationDbContext db) =>
            new(db, new AdminAuditService(db, NullLogger<AdminAuditService>.Instance))
            {
                ControllerContext = new ControllerContext
                {
                    HttpContext = new DefaultHttpContext
                    {
                        User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                        {
                            new Claim("user_id", "1"), new Claim("role", "admin"), new Claim(ClaimTypes.Role, "admin"), new Claim("email", "admin@test.local"),
                        }, "test")),
                    },
                },
            };

        private static ConcoursEventWriteRequest Valid() => new()
        {
            Slug = "FASA",
            Name = "  FASA (concours d'entrée)  ",
            Year = 2027,
            ExamDate = new DateTime(2027, 8, 20, 12, 0, 0, DateTimeKind.Unspecified),
            AdmissionConditions = "- BAC C, D ou E\n- 25 ans au plus",
            AdmissionConditionsEn = "- BAC C, D or E",
            Tracks = "Cycle ingénieur agronome",
            SourceUrl = " https://www.univ-dschang.org/concours \n\n https://minesup.gov.cm ",
            SourceCheckedAt = new DateTime(2026, 10, 20),
            FaqJson = "[{\"question\":\"Quand ?\",\"answer\":\"En août.\"}]",
            FaqJsonEn = "[{\"question\":\"When?\",\"answer\":\"In August.\"}]",
        };

        [Fact]
        public void AllAdminRoutes_RequireAdminPolicy()
        {
            var attr = typeof(AdminConcoursEventsController).GetCustomAttribute<AuthorizeAttribute>();
            Assert.NotNull(attr);
            Assert.Equal("AdminOnly", attr!.Policy);
            var anonymous = typeof(AdminConcoursEventsController).GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AllowAnonymousAttribute>() != null);
            Assert.Empty(anonymous);
            var route = typeof(AdminConcoursEventsController).GetCustomAttribute<RouteAttribute>();
            Assert.Equal("api/admin/concours-events", route!.Template);
        }

        [Fact]
        public async Task Create_Update_Publish_Unpublish_Delete()
        {
            using var db = TestApplicationDbContext.Create();
            var admin = Admin(db);

            var created = Body(await admin.Create(Valid())).GetProperty("data");
            var id = created.GetProperty("id").GetInt32();
            Assert.Equal("fasa", created.GetProperty("slug").GetString());
            Assert.Equal("FASA (concours d'entrée)", created.GetProperty("name").GetString());
            Assert.False(created.GetProperty("isPublished").GetBoolean()); // brouillon par défaut
            Assert.Equal("https://www.univ-dschang.org/concours\nhttps://minesup.gov.cm", created.GetProperty("sourceUrl").GetString());
            Assert.Equal(DateTimeKind.Utc, db.ConcoursEvents.Single().ExamDate!.Value.Kind);

            // Doublon slug + année refusé.
            Assert.IsType<ConflictObjectResult>(await admin.Create(Valid()));

            // Modification complète : un champ vidé redevient null.
            var update = Valid() with { Tracks = "  ", Coefficients = "Mathématiques : coef. 3", Location = "Dschang" };
            var updated = Body(await admin.Update(id, update)).GetProperty("data");
            Assert.Equal(JsonValueKind.Null, updated.GetProperty("tracks").ValueKind);
            Assert.Equal("Mathématiques : coef. 3", updated.GetProperty("coefficients").GetString());

            // Invisible sur l'API publique tant qu'il n'est pas publié.
            var pub = new ConcoursEventController(db);
            Assert.IsType<NotFoundResult>(await pub.Get("fasa", 2027));

            Assert.True(Body(await admin.Publish(id)).GetProperty("data").GetProperty("isPublished").GetBoolean());
            var visible = Body(await pub.Get("fasa", 2027));
            Assert.Equal("- BAC C, D ou E\n- 25 ans au plus", visible.GetProperty("admissionConditions").GetString());
            Assert.Equal("- BAC C, D or E", visible.GetProperty("admissionConditionsEn").GetString());
            Assert.Contains("When?", visible.GetProperty("faqJsonEn").GetString());
            Assert.Equal(new DateTime(2026, 10, 20), visible.GetProperty("sourceCheckedAt").GetDateTime().Date);

            Assert.False(Body(await admin.Unpublish(id)).GetProperty("data").GetProperty("isPublished").GetBoolean());
            Assert.IsType<NotFoundResult>(await pub.GetLatest("fasa"));

            // La liste admin montre aussi les brouillons, filtrée par examen et année.
            var list = Body(await admin.List("fasa", 2027)).GetProperty("data");
            Assert.Equal(1, list.GetArrayLength());
            Assert.Equal(0, Body(await admin.List("ibaf", 0)).GetProperty("data").GetArrayLength());

            Assert.IsType<OkObjectResult>(await admin.Delete(id));
            Assert.Empty(db.ConcoursEvents);
            Assert.IsType<NotFoundObjectResult>(await admin.Delete(id));
            // Chaque écriture est tracée.
            Assert.Equal(new[] { "concours_event.create", "concours_event.update", "concours_event.publish", "concours_event.unpublish", "concours_event.delete" },
                db.AdminAuditLogs.OrderBy(a => a.Id).Select(a => a.Action).ToArray());
        }

        [Theory]
        [InlineData("programme")]
        [InlineData("conditions")]
        [InlineData("coefficients")]
        [InlineData("date")]
        [InlineData("programmeEn")]
        public void OfficialInformation_RequiresSourceUrlAndDate(string field)
        {
            var r = new ConcoursEventWriteRequest { Slug = "ens", Name = "ENS", Year = 2027 };
            r = field switch
            {
                "programme" => r with { Programme = "Terminale C" },
                "conditions" => r with { AdmissionConditions = "BAC" },
                "coefficients" => r with { Coefficients = "Maths 4" },
                "date" => r with { RegistrationEndDate = DateTime.UtcNow },
                _ => r with { ProgrammeEn = "Upper sixth" },
            };
            var errors = ConcoursEventValidation.Validate(r);
            Assert.Contains(errors, e => e.StartsWith("Source : obligatoire"));
            Assert.Contains(errors, e => e.Contains("date de consultation"));

            Assert.Empty(ConcoursEventValidation.Validate(r with { SourceUrl = "https://ens-yde.cm", SourceCheckedAt = DateTime.UtcNow.Date }));
        }

        [Fact]
        public void Validation_RejectsBadInput()
        {
            Assert.Empty(ConcoursEventValidation.Validate(new ConcoursEventWriteRequest { Slug = "iut", Name = "IUT", Year = 2027, Tracks = "GIM" }));

            var errors = ConcoursEventValidation.Validate(new ConcoursEventWriteRequest
            {
                Slug = "IUT Douala!", Name = " ", Year = 1990,
                RegistrationStartDate = new DateTime(2027, 3, 1), RegistrationEndDate = new DateTime(2027, 2, 1),
                SourceUrl = "ftp://x\nhttps://ok.cm", SourceCheckedAt = DateTime.UtcNow.AddDays(10),
                OfficialRegistrationUrl = "www.sans-schema.cm", EnrollmentFeeXaf = -5,
                FaqJson = "{\"question\":\"x\"}", FaqJsonEn = "[{\"question\":\"x\",\"answer\":\"\"}]",
            });
            Assert.Contains(errors, e => e.StartsWith("Examen"));
            Assert.Contains(errors, e => e.StartsWith("Nom"));
            Assert.Contains(errors, e => e.StartsWith("Année"));
            Assert.Contains(errors, e => e.StartsWith("Dates"));
            Assert.Contains(errors, e => e.Contains("chaque ligne"));
            Assert.Contains(errors, e => e.Contains("futur"));
            Assert.Contains(errors, e => e.StartsWith("Site d'inscription"));
            Assert.Contains(errors, e => e.StartsWith("Frais"));
            Assert.Contains(errors, e => e.StartsWith("FAQ :"));
            Assert.Contains(errors, e => e.StartsWith("FAQ anglaise"));
        }

        [Fact]
        public async Task Publish_RefusesLegacyRowWithDatesButNoSource()
        {
            using var db = TestApplicationDbContext.Create();
            db.ConcoursEvents.Add(new ConcoursEvent { Id = 7, Slug = "bac", Name = "Baccalauréat", Year = 2027, ExamDate = DateTime.UtcNow, IsPublished = false });
            await db.SaveChangesAsync();

            var res = await Admin(db).Publish(7);
            Assert.IsType<BadRequestObjectResult>(res);
            Assert.False(db.ConcoursEvents.Single().IsPublished);
            // Dépublier reste toujours possible.
            Assert.IsType<OkObjectResult>(await Admin(db).Unpublish(7));
        }

        [Fact]
        public async Task ContentEndpoint_RejectsMalformedFaq_AndStoresEnglish()
        {
            using var db = TestApplicationDbContext.Create();
            db.ConcoursEvents.Add(new ConcoursEvent { Id = 3, Slug = "ens", Name = "ENS", Year = 2027 });
            await db.SaveChangesAsync();
            var pub = new ConcoursEventController(db);

            Assert.IsType<BadRequestObjectResult>(await pub.UpdateContent(3, new UpdateConcoursContentRequest(null, "pas du json")));
            Assert.IsType<OkObjectResult>(await pub.UpdateContent(3, new UpdateConcoursContentRequest("Conseil", null, "Tip", "[{\"question\":\"Q\",\"answer\":\"A\"}]")));
            var ev = db.ConcoursEvents.Single();
            Assert.Equal("Tip", ev.TipsEn);
            Assert.Contains("\"Q\"", ev.FaqJsonEn);
        }
    }
}
