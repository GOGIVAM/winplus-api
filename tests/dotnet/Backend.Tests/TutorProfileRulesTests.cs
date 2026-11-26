using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Backend.Tests;

/// <summary>Profil répétiteur (26/11/2026) : minimum à l'activation, recherche, désactivation, bornes, champs vidables.</summary>
public class TutorProfileRulesTests
{
    private static async Task<(TestApplicationDbContext Db, TutorProfileService Svc, int UserId)> WorldAsync(bool photo = true, bool complete = true)
    {
        var db = TestApplicationDbContext.Create();
        var user = new User { Email = $"t{System.Guid.NewGuid():N}@it.local", Role = "teacher", IsActive = true, FirstName = "Awa", LastName = "T",
                              AvatarUrl = photo ? "https://example.test/a.jpg" : null };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        var profile = new TutorProfile { UserId = user.Id, NoticeHours = 0 };
        if (complete)
        {
            profile.HourlyRateXaf = 5000;
            profile.OffersOnline = true;
            profile.Subjects = new List<TutorSubject> { new() { Subject = "Maths" } };
            profile.Levels = new List<TutorLevel> { new() { Level = "Terminale" } };
        }
        db.TutorProfiles.Add(profile);
        await db.SaveChangesAsync();
        return (db, new TutorProfileService(db, NullLogger<TutorProfileService>.Instance), user.Id);
    }

    [Fact]
    public async Task Activation_refusee_avec_un_code_par_element_manquant()
    {
        var (_, svc, uid) = await WorldAsync(photo: false, complete: false);
        var ex = await Assert.ThrowsAsync<TutorProfileRuleException>(() => svc.ActivateAsync(uid));
        Assert.Equal("profil_incomplet", ex.Code);
        Assert.Equal(new[] { "photo", "subjects", "levels", "interventionMode", "hourlyRate" }, ex.Missing!.Select(m => m.Field));
    }

    [Fact]
    public async Task Activation_puis_recherche_puis_retrait_si_minimum_casse()
    {
        var (db, svc, uid) = await WorldAsync();
        var dto = await svc.ActivateAsync(uid);
        Assert.True(dto.IsActive);
        Assert.True(dto.IsListedInSearch);
        Assert.Contains(await svc.SearchAsync(null, null, null, false, 1, 20), r => r.UserId == uid);

        // Matières vidées après l'activation : le profil reste actif mais sort de la recherche.
        dto = await svc.UpdateAsync(uid, new UpdateTutorProfileRequestDto { Subjects = new List<string>() });
        Assert.True(dto.IsActive);
        Assert.False(dto.IsListedInSearch);
        Assert.DoesNotContain(await svc.SearchAsync(null, null, null, false, 1, 20), r => r.UserId == uid);

        // Compte suspendu : hors recherche et fiche publique fermée.
        await svc.UpdateAsync(uid, new UpdateTutorProfileRequestDto { Subjects = new List<string> { "Maths" } });
        var user = await db.Users.FindAsync(uid);
        user!.IsActive = false;
        await db.SaveChangesAsync();
        Assert.DoesNotContain(await svc.SearchAsync(null, null, null, false, 1, 20), r => r.UserId == uid);
        Assert.Null(await svc.GetPublicProfileAsync(uid));
    }

    [Fact]
    public async Task Desactivation_retire_le_profil()
    {
        var (_, svc, uid) = await WorldAsync();
        await svc.ActivateAsync(uid);
        var dto = await svc.DeactivateAsync(uid);
        Assert.False(dto.IsActive);
        Assert.Null(await svc.GetPublicProfileAsync(uid));
    }

    [Theory]
    [InlineData("tarif_invalide")]
    [InlineData("prix_essai_invalide")]
    [InlineData("plafond_seances_invalide")]
    [InlineData("video_invalide")]
    [InlineData("champ_inconnu")]
    public async Task Bornes_refusees_avec_code(string code)
    {
        var (_, svc, uid) = await WorldAsync();
        var req = code switch
        {
            "tarif_invalide" => new UpdateTutorProfileRequestDto { HourlyRateXaf = 100 },
            "prix_essai_invalide" => new UpdateTutorProfileRequestDto { TrialSessionPriceXaf = 9000 },
            "plafond_seances_invalide" => new UpdateTutorProfileRequestDto { MaxSessionsPerWeek = 0 },
            "video_invalide" => new UpdateTutorProfileRequestDto { VideoUrl = "javascript:alert(1)" },
            _ => new UpdateTutorProfileRequestDto { Clear = new List<string> { "subjects" } },
        };
        var ex = await Assert.ThrowsAsync<TutorProfileRuleException>(() => svc.UpdateAsync(uid, req));
        Assert.Equal(code, ex.Code);
    }

    [Fact]
    public async Task Valeurs_vidables_explicitement()
    {
        var (_, svc, uid) = await WorldAsync();
        await svc.UpdateAsync(uid, new UpdateTutorProfileRequestDto { MaxSessionsPerWeek = 5, TrialSessionPriceXaf = 2000, VideoUrl = "https://youtu.be/abc", Title = "Prof" });
        var dto = await svc.UpdateAsync(uid, new UpdateTutorProfileRequestDto
        {
            Clear = new List<string> { "maxSessionsPerWeek", "trialSessionPrice", "videoUrl" },
            Title = "",
        });
        Assert.Null(dto.MaxSessionsPerWeek);
        Assert.Null(dto.TrialSessionPriceXaf);
        Assert.Null(dto.VideoUrl);
        Assert.Null(dto.Title);
    }

    [Fact]
    public async Task Creneaux_non_plafonnes_par_le_maximum_de_seances()
    {
        var (_, svc, uid) = await WorldAsync();
        await svc.UpdateAsync(uid, new UpdateTutorProfileRequestDto { MaxSessionsPerWeek = 1 });
        var slots = Enumerable.Range(0, 3).Select(d => new TutorAvailabilitySlotDto { DayOfWeek = d, StartTime = "10:00", EndTime = "11:00", IsActive = true }).ToList();
        var dto = await svc.UpdateAvailabilityAsync(uid, slots);
        Assert.Equal(3, dto.AvailabilitySlots.Count);
    }
}
