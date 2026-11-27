using System.Collections.Generic;
using System.Threading.Tasks;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services;
using Xunit;

namespace Backend.Tests;

/// <summary>T7 (séance d'essai) et T8 (lieu du présentiel).</summary>
public class TutorTrialAndLocationTests
{
    private static TutorProfile Profile(bool trial = true, decimal? price = null, string? hint = "Bonamoussadi, près du carrefour") =>
        new() { Id = 1, UserId = 10, TrialSessionEnabled = trial, TrialSessionPriceXaf = price, TutorHomeAddressHint = hint };

    [Fact]
    public void Essai_refuse_si_non_propose_collectif_ou_trop_long()
    {
        Assert.Equal("essai_non_disponible", Assert.Throws<TutorBookingRuleException>(() => TutorTrial.EnsureOffered(Profile(trial: false), 1, 60)).Code);
        Assert.Equal("essai_non_disponible", Assert.Throws<TutorBookingRuleException>(() => TutorTrial.EnsureOffered(Profile(), 2, 60)).Code);
        Assert.Equal("essai_duree", Assert.Throws<TutorBookingRuleException>(() => TutorTrial.EnsureOffered(Profile(), 1, 90)).Code);
        TutorTrial.EnsureOffered(Profile(), 1, 60);
    }

    [Theory]
    [InlineData(null, 0, "Séance d'essai gratuite")]
    [InlineData(1500, 1500, "Séance d'essai")]
    public void Prix_d_essai_applique(int? trialPrice, int expected, string label)
    {
        var q = new TutorPriceQuoteDto { TotalXaf = 6000, PerStudentXaf = 6000 };
        TutorTrial.Apply(q, trialPrice);
        Assert.True(q.IsTrial);
        Assert.Equal(expected, q.TotalXaf);
        Assert.Equal(expected, q.PerStudentXaf);
        Assert.Equal(6000, q.RegularTotalXaf);
        Assert.Equal(label, q.TrialLabel);
    }

    [Fact]
    public async Task Essai_seulement_pour_une_premiere_rencontre()
    {
        var db = TestApplicationDbContext.Create();
        Assert.False(await TutorTrial.HasHistoryAsync(db, 1, new[] { 5 }, 5));
        db.TutorBookings.Add(new TutorBooking { TutorProfileId = 1, StudentUserId = 5, Status = "cancelled" });
        await db.SaveChangesAsync();
        Assert.False(await TutorTrial.HasHistoryAsync(db, 1, new[] { 5 }, 5)); // annulée : ne compte pas
        db.TutorBookings.Add(new TutorBooking { TutorProfileId = 1, StudentUserId = 7, PayerUserId = 3, Status = "completed" });
        await db.SaveChangesAsync();
        Assert.True(await TutorTrial.HasHistoryAsync(db, 1, new[] { 8 }, 3));  // même payeur (parent)
        Assert.True(await TutorTrial.HasHistoryAsync(db, 1, new[] { 7 }, 9));  // même élève
        Assert.False(await TutorTrial.HasHistoryAsync(db, 2, new[] { 7 }, 3)); // autre tuteur
    }

    [Fact]
    public void Lieu_exige_repris_ignore_et_filtre()
    {
        Assert.Equal("lieu_requis", Assert.Throws<TutorBookingRuleException>(() => TutorSessionLocation.Resolve(Profile(), "student_home", " ", null)).Code);
        Assert.Equal("lieu_requis", Assert.Throws<TutorBookingRuleException>(() => TutorSessionLocation.Resolve(Profile(), "neutral_place", null, null)).Code);
        Assert.Equal(("Bonamoussadi, près du carrefour", (string?)null), TutorSessionLocation.Resolve(Profile(), "tutor_home", null, null));
        Assert.Equal(((string?)null, (string?)null), TutorSessionLocation.Resolve(Profile(), "online", "Akwa", "2e étage"));
        Assert.Equal(("Rue 1.234, Akwa, face à la pharmacie", "Portail bleu"),
            TutorSessionLocation.Resolve(Profile(), "student_home", "  Rue 1.234, Akwa, face à la pharmacie ", "Portail bleu"));
        Assert.Equal("coordonnees_interdites", Assert.Throws<TutorBookingRuleException>(
            () => TutorSessionLocation.Resolve(Profile(), "student_home", "Akwa, appelle au 699123456", null)).Code);
        Assert.Equal(200, TutorSessionLocation.Resolve(Profile(), "student_home", new string('a', 300), null).Address!.Length);
    }
}
