using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Backend.Data;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Controllers;

/// <summary>
/// Cours en direct sur la visio WinPlus (lot 4 du plan temps réel, 24/11/2026) : Jitsi Meet auto-hébergé
/// sur visio.winplus.cm, accessible seulement avec un jeton JWT signé ici.
///
/// Deux types de séances :
///   seance  : séance d'enseignement en direct (<see cref="Models.Entities.Session"/>, Type = live) ;
///             le professeur qui l'a créée anime, les élèves inscrits (gratuit ou payé) participent.
///   tutorat : réservation de répétiteur en ligne (<see cref="Models.Entities.TutorBooking"/>, Mode = online,
///             individuelle ou de groupe) ; le répétiteur anime, les élèves participants suivent.
///
/// L'animateur reçoit le rôle de modérateur par son jeton (module token_affiliation côté Jitsi), quel que soit
/// l'ordre d'arrivée. Le jeton ne vaut que pour la salle de la séance et expire après sa fin.
///
/// Configuration : Visio:Domain (visio.winplus.cm), Visio:AppId (winplus), Visio:AppSecret (secret partagé
/// avec Jitsi, variable d'environnement Visio__AppSecret). Sans secret, la visio est désactivée (503).
/// </summary>
[ApiController]
[Route("api/visio")]
[Authorize]
public class VisioController : ControllerBase
{
    /// <summary>Ouverture de la salle avant le début, et marge après la fin (débordement, questions).</summary>
    private static readonly TimeSpan OuvertureAvant = TimeSpan.FromMinutes(30);
    private static readonly TimeSpan MargeApres = TimeSpan.FromMinutes(60);

    private static readonly string[] StatutsTutoratOuverts = { "confirmed", "completed" };
    private static readonly string[] InscriptionsValides = { "free", "paid" };

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;

    public VisioController(ApplicationDbContext db, IConfiguration config)
    {
        _db = db; _config = config;
    }

    /// <summary>
    /// Jeton d'accès à la salle d'une séance. 404 si la séance n'existe pas ou si l'utilisateur n'y a pas accès
    /// (on ne révèle pas son existence), 409 si la salle n'est pas encore ou n'est plus ouverte.
    /// </summary>
    [HttpGet("jeton")]
    public async Task<IActionResult> Jeton([FromQuery] string type, [FromQuery] int id)
    {
        var secret = _config["Visio:AppSecret"];
        if (string.IsNullOrWhiteSpace(secret))
            return StatusCode(503, new { success = false, error = "La visio n'est pas configurée." });

        var me = User.GetUserId();
        Seance? seance = type switch
        {
            "seance" => await SeanceEnseignement(id, me),
            "tutorat" => await SeanceTutorat(id, me),
            _ => null,
        };
        if (seance == null)
            return NotFound(new { success = false, error = "Séance introuvable." });

        var now = DateTime.UtcNow;
        if (now < seance.Debut - OuvertureAvant)
            return Conflict(new { success = false, code = "pas_encore", ouverture = seance.Debut - OuvertureAvant,
                error = "La salle ouvre 30 minutes avant le début de la séance." });
        if (now > seance.Fin + MargeApres)
            return Conflict(new { success = false, code = "terminee", error = "Cette séance est terminée." });

        var utilisateur = await _db.Users.AsNoTracking().Where(u => u.Id == me)
            .Select(u => new { u.FirstName, u.LastName, u.Username, u.AvatarUrl }).FirstAsync();
        var nom = $"{utilisateur.FirstName} {utilisateur.LastName}".Trim();
        if (nom.Length == 0) nom = utilisateur.Username ?? $"Utilisateur {me}";

        var salle = $"winplus-{type}-{id}";
        var expiration = seance.Fin + MargeApres + TimeSpan.FromMinutes(30);
        var jeton = Signer(secret, salle, me, nom, utilisateur.AvatarUrl, seance.Animateur, now, expiration);

        return Ok(new
        {
            success = true,
            data = new
            {
                domaine = _config["Visio:Domain"] ?? "visio.winplus.cm",
                salle,
                jeton,
                moderateur = seance.Animateur,
                titre = seance.Titre,
                debut = seance.Debut,
                fin = seance.Fin,
                nom,
            },
        });
    }

    private sealed record Seance(string Titre, DateTime Debut, DateTime Fin, bool Animateur);

    private async Task<Seance?> SeanceEnseignement(int id, int me)
    {
        var s = await _db.Sessions.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDeleted && x.Type == "live" && x.Status != "cancelled")
            .Select(x => new { x.Title, x.StartDate, x.EndDate, x.CreatedBy }).FirstOrDefaultAsync();
        if (s == null) return null;
        var animateur = s.CreatedBy == me;
        if (!animateur && !await _db.SessionEnrollments.AsNoTracking()
                .AnyAsync(e => e.SessionId == id && e.StudentId == me && InscriptionsValides.Contains(e.PaymentStatus)))
            return null;
        return new Seance(s.Title, DateTime.SpecifyKind(s.StartDate, DateTimeKind.Utc), DateTime.SpecifyKind(s.EndDate, DateTimeKind.Utc), animateur);
    }

    private async Task<Seance?> SeanceTutorat(int id, int me)
    {
        var b = await _db.TutorBookings.AsNoTracking()
            .Include(x => x.TutorProfile)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == id && x.Mode == "online" && StatutsTutoratOuverts.Contains(x.Status));
        if (b == null) return null;
        var animateur = b.TutorProfile?.UserId == me;
        var participant = b.StudentUserId == me || b.Participants.Any(p => p.StudentUserId == me);
        if (!animateur && !participant) return null;
        // Même convention que TutorBookingService : date et heures de la réservation lues en UTC.
        var debut = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
        var fin = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime), DateTimeKind.Utc);
        var titre = string.IsNullOrWhiteSpace(b.Subject) ? "Séance de tutorat" : $"Tutorat : {b.Subject}";
        return new Seance(titre, debut, fin, animateur);
    }

    /// <summary>Jeton au format attendu par Jitsi (prosody mod_auth_token, HS256).</summary>
    private string Signer(string secret, string salle, int userId, string nom, string? avatar, bool moderateur,
        DateTime maintenant, DateTime expiration)
    {
        var appId = _config["Visio:AppId"] ?? "winplus";
        var contexte = new Dictionary<string, object?>
        {
            ["user"] = new Dictionary<string, object?>
            {
                ["id"] = userId.ToString(),
                ["name"] = nom,
                ["avatar"] = string.IsNullOrWhiteSpace(avatar) ? null : avatar,
                ["moderator"] = moderateur,
                ["affiliation"] = moderateur ? "owner" : "member",
            },
        };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = appId,
            ["aud"] = "jitsi",
            ["sub"] = "*",
            ["room"] = salle,
            ["nbf"] = new DateTimeOffset(maintenant.AddMinutes(-5)).ToUnixTimeSeconds(),
            ["iat"] = new DateTimeOffset(maintenant).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(expiration).ToUnixTimeSeconds(),
            ["context"] = contexte,
        };
        var entete = B64(JsonSerializer.SerializeToUtf8Bytes(new { alg = "HS256", typ = "JWT" }));
        var corps = B64(JsonSerializer.SerializeToUtf8Bytes(payload));
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var signature = B64(hmac.ComputeHash(Encoding.ASCII.GetBytes($"{entete}.{corps}")));
        return $"{entete}.{corps}.{signature}";
    }

    private static string B64(byte[] octets) => Base64UrlEncoder.Encode(octets);
}
