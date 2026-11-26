using System;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using Backend.Controllers;
using Backend.Models.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests;

/// <summary>Alertes d'inactivité du parent : écartées après reprise, durée recalculée, une seule à la fois.</summary>
public class ParentAlertFreshnessTests
{
    private static (ParentAlertController Ctrl, TestApplicationDbContext Db, int ChildId) Setup()
    {
        var db = TestApplicationDbContext.Create();
        var parent = new User { Email = "p@it.local", Role = "parent", IsActive = true, FirstName = "Paul" };
        var child = new User { Email = "c@it.local", Role = "student", IsActive = true, FirstName = "Gaël" };
        db.Users.AddRange(parent, child);
        db.SaveChanges();
        db.ParentStudentLinks.Add(new ParentStudentLink { ParentId = parent.Id, StudentId = child.Id, Status = "accepted" });
        var now = DateTime.UtcNow;
        db.ParentAlerts.AddRange(
            new ParentAlert { ParentId = parent.Id, ChildId = child.Id, Type = "Inactivite", Severity = "High", Content = "Gaël n'a pas eu d'activité depuis 14 jours.", DetectedAt = now.AddDays(-10) },
            new ParentAlert { ParentId = parent.Id, ChildId = child.Id, Type = "Inactivite", Severity = "High", Content = "Gaël n'a pas eu d'activité depuis 14 jours.", DetectedAt = now.AddDays(-3) },
            new ParentAlert { ParentId = parent.Id, ChildId = child.Id, Type = "BaissePerformance", Severity = "Medium", Content = "Baisse.", DetectedAt = now.AddDays(-2) });
        db.SaveChanges();
        var ctrl = new ParentAlertController(db, NullLogger<ParentAlertController>.Instance)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim("user_id", parent.Id.ToString()), new Claim("role", "parent") }, "t")),
                },
            },
        };
        return (ctrl, db, child.Id);
    }

    private static JsonElement Json(IActionResult r) =>
        JsonSerializer.SerializeToElement(((ObjectResult)r).Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));

    [Fact]
    public async Task Inactivite_reecrite_avec_la_duree_reelle_et_non_repetee()
    {
        var (ctrl, db, child) = Setup();
        db.QuizAttempts.Add(new QuizAttempt { UserId = child, QuizId = 1, Score = 50, CompletedAt = DateTime.UtcNow.AddDays(-20) });
        await db.SaveChangesAsync();

        var list = Json(await ctrl.GetAlerts(child, null)).EnumerateArray().ToList();
        var inact = list.Where(a => a.GetProperty("type").GetString() == "Inactivite").ToList();
        Assert.Single(inact);
        Assert.Equal(20, inact[0].GetProperty("inactivityDays").GetInt32());
        Assert.Contains("depuis 20 jours", inact[0].GetProperty("content").GetString());

        var signals = Json(await ctrl.GetBarometre(child)).GetProperty("signals").EnumerateArray().ToList();
        Assert.Equal(2, signals.Count);
    }

    [Fact]
    public async Task Inactivite_ecartee_apres_reprise()
    {
        var (ctrl, db, child) = Setup();
        db.QuizAttempts.Add(new QuizAttempt { UserId = child, QuizId = 1, Score = 80, CompletedAt = DateTime.UtcNow.AddHours(-2) });
        await db.SaveChangesAsync();

        var list = Json(await ctrl.GetAlerts(child, null)).EnumerateArray().ToList();
        Assert.DoesNotContain(list, a => a.GetProperty("type").GetString() == "Inactivite");
        Assert.Contains(list, a => a.GetProperty("type").GetString() == "BaissePerformance");
    }
}
