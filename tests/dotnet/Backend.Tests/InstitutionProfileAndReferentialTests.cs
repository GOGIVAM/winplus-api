using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Backend.Tests;

/// <summary>Fiche de l'établissement (GET/PUT /api/institution/profile) et référentiel des matières et niveaux.</summary>
public class InstitutionProfileAndReferentialTests
{
    [Theory]
    [InlineData("mathématiques", "Mathématiques")]
    [InlineData("Maths", "Mathématiques")]
    [InlineData("ENSP Polytechnique", "ENSP Yaoundé (ENSPY)")]
    [InlineData("  terminale ", "Terminale")]
    [InlineData("Économie", "Économie")]
    [InlineData("Robotique", "Robotique")]
    public void Libelles_ramenes_au_referentiel(string saisi, string attendu) =>
        Assert.Equal(attendu, TutorReferential.Canonical(saisi));

    private static (InstitutionProfileController Ctrl, TestApplicationDbContext Db, Mock<INtfyService> Ntfy, int InstId) Setup(string role = "institution", bool attached = true)
    {
        var db = TestApplicationDbContext.Create();
        var inst = new Institution { Name = "Lycée Test", Country = "CM", City = "Douala", Phone = "699000000", Type = "lycee", Cycles = "secondaire" };
        db.Institutions.Add(inst);
        db.SaveChanges();
        var user = new User { Email = "etab@it.local", Role = role, IsActive = true, InstitutionId = attached ? inst.Id : null, AvatarUrl = "https://x/logo.png" };
        db.Users.Add(user);
        db.SaveChanges();
        var ntfy = new Mock<INtfyService>();
        var ctrl = new InstitutionProfileController(db, ntfy.Object, NullLogger<InstitutionProfileController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()), new Claim("sub", user.Id.ToString()),
                        new Claim("userId", user.Id.ToString()), new Claim(ClaimTypes.Role, role), new Claim("role", role),
                    }, "test")),
                },
            },
        };
        return (ctrl, db, ntfy, inst.Id);
    }

    private static JsonElement Body(IActionResult r) =>
        JsonSerializer.SerializeToElement(((ObjectResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Lecture_de_la_fiche()
    {
        var (ctrl, _, _, id) = Setup();
        var b = Body(await ctrl.Get());
        Assert.Equal(id, b.GetProperty("data").GetProperty("id").GetInt32());
        Assert.Equal("https://x/logo.png", b.GetProperty("data").GetProperty("logoUrl").GetString());
        Assert.Equal("secondaire", b.GetProperty("data").GetProperty("cycles")[0].GetString());
    }

    [Fact]
    public async Task Modification_telephone_et_ville_nom_soumis_a_validation()
    {
        var (ctrl, db, ntfy, id) = Setup();
        var r = await ctrl.Update(new InstitutionProfileController.UpdateInstitutionProfileRequest
        {
            Phone = "+237 677 11 22 33", City = "Yaoundé", Name = "Lycée Renommé", Cycles = new List<string> { "secondaire", "superieur" },
        });
        var b = Body(r);
        Assert.True(b.GetProperty("nameChangeRequested").GetBoolean());
        var inst = await db.Institutions.FindAsync(id);
        Assert.Equal("+237 677 11 22 33", inst!.Phone);
        Assert.Equal("Yaoundé", inst.City);
        Assert.Equal("Lycée Test", inst.Name);
        Assert.Equal("secondaire,superieur", inst.Cycles);
        ntfy.Verify(n => n.PublishAdminAsync(It.IsAny<string>(), It.Is<string>(m => m.Contains("Lycée Renommé")),
            It.IsAny<string>(), It.IsAny<string[]?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<int?>()), Times.Once);
    }

    [Theory]
    [InlineData("type_invalide")]
    [InlineData("telephone_invalide")]
    [InlineData("email_invalide")]
    [InlineData("ville_requise")]
    public async Task Refus_a_code(string code)
    {
        var (ctrl, _, _, _) = Setup();
        var req = code switch
        {
            "type_invalide" => new InstitutionProfileController.UpdateInstitutionProfileRequest { Type = "castle" },
            "telephone_invalide" => new InstitutionProfileController.UpdateInstitutionProfileRequest { Phone = "appelez-moi" },
            "email_invalide" => new InstitutionProfileController.UpdateInstitutionProfileRequest { Email = "pas-un-mail" },
            _ => new InstitutionProfileController.UpdateInstitutionProfileRequest { City = "  " },
        };
        var r = (ObjectResult)await ctrl.Update(req);
        Assert.Equal(400, r.StatusCode);
        Assert.Equal(code, Body(r).GetProperty("code").GetString());
    }

    [Fact]
    public async Task Reserve_au_compte_etablissement_rattache()
    {
        var (other, _, _, _) = Setup(role: "teacher");
        Assert.Equal(403, ((ObjectResult)await other.Get()).StatusCode);
        var (orphan, _, _, _) = Setup(attached: false);
        Assert.Equal(404, ((ObjectResult)await orphan.Get()).StatusCode);
    }
}
