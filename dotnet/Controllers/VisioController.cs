using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Amazon.S3;
using Amazon.S3.Model;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.TempsReel;
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
///   seance  : séance d'enseignement en direct (<see cref="Session"/>, Type = live) ;
///             le professeur qui l'a créée anime, les élèves inscrits (gratuit ou payé) participent.
///   tutorat : réservation de répétiteur en ligne (<see cref="TutorBooking"/>, Mode = online,
///             individuelle ou de groupe) ; le répétiteur anime, les élèves participants suivent.
///
/// L'animateur reçoit le rôle de modérateur par son jeton (module token_affiliation côté Jitsi), quel que soit
/// l'ordre d'arrivée. Le jeton ne vaut que pour la salle de la séance et expire après sa fin.
///
/// Appel : la page signale arrivée, présence (chaque minute) et départ ; l'animateur voit la liste des élèves
/// attendus (présents, absents, minutes passées), tenue à jour par l'événement temps réel « visio-presence ».
///
/// Enregistrements : l'animateur enregistre dans Jitsi (enregistrement local), puis dépose le fichier ;
/// le navigateur l'envoie directement dans le stockage privé S3 (URL présignée, préfixe private/visio/).
/// Animateur et participants le revoient par un lien temporaire.
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
    /// <summary>Sans signal depuis ce délai, une présence est considérée terminée (onglet fermé, coupure).</summary>
    private static readonly TimeSpan SignalPerdu = TimeSpan.FromMinutes(3);
    /// <summary>Taille maximale d'un enregistrement déposé (2 h de 720p en WebM tiennent largement).</summary>
    private const long TailleMaxEnregistrement = 4L * 1024 * 1024 * 1024;
    private static readonly string[] TypesVideo = { "video/webm", "video/mp4", "video/x-matroska" };

    private static readonly string[] StatutsTutoratOuverts = { "confirmed", "completed" };
    private static readonly string[] InscriptionsValides = { "free", "paid" };

    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _config;
    private readonly IStorageService _storage;
    private readonly ITempsReel _tempsReel;
    private readonly ILogger<VisioController> _logger;

    public VisioController(ApplicationDbContext db, IConfiguration config, IStorageService storage, ITempsReel tempsReel,
        ILogger<VisioController> logger)
    {
        _db = db; _config = config; _storage = storage; _tempsReel = tempsReel; _logger = logger;
    }

    // ───────────────────────── Accès à une séance ─────────────────────────

    private sealed record Seance(string Titre, DateTime Debut, DateTime Fin, int AnimateurId, bool Animateur, IReadOnlyList<int> Participants);

    private static bool TypeValide(string? type) => type is "seance" or "tutorat";

    /// <summary>La séance, si l'utilisateur l'anime ou y participe (sans contrôle d'horaire) ; sinon null.</summary>
    private async Task<Seance?> Charger(string type, int id, int me) => type switch
    {
        "seance" => await SeanceEnseignement(id, me),
        "tutorat" => await SeanceTutorat(id, me),
        _ => null,
    };

    private async Task<Seance?> SeanceEnseignement(int id, int me)
    {
        var s = await _db.Sessions.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDeleted && x.Type == "live" && x.Status != "cancelled")
            .Select(x => new { x.Title, x.StartDate, x.EndDate, x.CreatedBy }).FirstOrDefaultAsync();
        if (s == null || s.CreatedBy == null) return null;
        var inscrits = await _db.SessionEnrollments.AsNoTracking()
            .Where(e => e.SessionId == id && InscriptionsValides.Contains(e.PaymentStatus))
            .Select(e => e.StudentId).Distinct().ToListAsync();
        var animateur = s.CreatedBy == me;
        if (!animateur && !inscrits.Contains(me)) return null;
        return new Seance(s.Title, DateTime.SpecifyKind(s.StartDate, DateTimeKind.Utc), DateTime.SpecifyKind(s.EndDate, DateTimeKind.Utc),
            s.CreatedBy.Value, animateur, inscrits);
    }

    private async Task<Seance?> SeanceTutorat(int id, int me)
    {
        var b = await _db.TutorBookings.AsNoTracking()
            .Include(x => x.TutorProfile)
            .Include(x => x.Participants)
            .FirstOrDefaultAsync(x => x.Id == id && x.Mode == "online" && StatutsTutoratOuverts.Contains(x.Status));
        if (b?.TutorProfile == null) return null;
        var participants = b.Participants.Count > 0
            ? b.Participants.Select(p => p.StudentUserId).Distinct().ToList()
            : new List<int> { b.StudentUserId };
        var animateur = b.TutorProfile.UserId == me;
        if (!animateur && b.StudentUserId != me && !participants.Contains(me)) return null;
        // Même convention que TutorBookingService : date et heures de la réservation lues en UTC.
        var debut = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
        var fin = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime), DateTimeKind.Utc);
        var titre = string.IsNullOrWhiteSpace(b.Subject) ? "Séance de tutorat" : $"Tutorat : {b.Subject}";
        return new Seance(titre, debut, fin, b.TutorProfile.UserId, animateur, participants);
    }

    private NotFoundObjectResult Introuvable() => NotFound(new { success = false, error = "Séance introuvable." });

    // ───────────────────────── Jeton de la salle ─────────────────────────

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
        var seance = TypeValide(type) ? await Charger(type, id, me) : null;
        if (seance == null) return Introuvable();

        var now = DateTime.UtcNow;
        if (now < seance.Debut - OuvertureAvant)
            return Conflict(new { success = false, code = "pas_encore", ouverture = seance.Debut - OuvertureAvant,
                error = "La salle ouvre 30 minutes avant le début de la séance." });
        if (now > seance.Fin + MargeApres)
            return Conflict(new { success = false, code = "terminee", error = "Cette séance est terminée." });

        var utilisateur = await _db.Users.AsNoTracking().Where(u => u.Id == me)
            .Select(u => new { u.FirstName, u.LastName, u.Username, u.AvatarUrl }).FirstAsync();
        var nom = NomAffiche(utilisateur.FirstName, utilisateur.LastName, utilisateur.Username, me);

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

    private static string NomAffiche(string? prenom, string? nom, string? username, int id)
    {
        var complet = $"{prenom} {nom}".Trim();
        return complet.Length > 0 ? complet : username ?? $"Utilisateur {id}";
    }

    // ───────────────────────── Appel (présences) ─────────────────────────

    public sealed class PresenceRequest
    {
        public string Type { get; set; } = "";
        public int Id { get; set; }
        /// <summary>arrivee | present | depart</summary>
        public string Evenement { get; set; } = "";
        /// <summary>Id renvoyé par l'arrivée, pour les signaux suivants.</summary>
        public int? PresenceId { get; set; }
    }

    /// <summary>Signal de présence dans la salle (arrivée, toujours là chaque minute, départ).</summary>
    [HttpPost("presence")]
    public async Task<IActionResult> Presence([FromBody] PresenceRequest req)
    {
        if (!TypeValide(req.Type) || req.Evenement is not ("arrivee" or "present" or "depart"))
            return BadRequest(new { success = false, error = "Signal de présence invalide." });
        var me = User.GetUserId();
        var seance = await Charger(req.Type, req.Id, me);
        if (seance == null) return Introuvable();
        var now = DateTime.UtcNow;

        VisioPresence? p = null;
        if (req.PresenceId is int pid)
            p = await _db.VisioPresences.FirstOrDefaultAsync(x => x.Id == pid && x.UserId == me && x.Type == req.Type && x.RefId == req.Id);

        if (req.Evenement == "arrivee" || p == null)
        {
            if (req.Evenement == "depart") return Ok(new { success = true, data = new { presenceId = (int?)null } });
            p = new VisioPresence { Type = req.Type, RefId = req.Id, UserId = me, IsHost = seance.Animateur, JoinedAt = now, LastSeenAt = now };
            _db.VisioPresences.Add(p);
        }
        else
        {
            p.LastSeenAt = now;
            if (req.Evenement == "depart") p.LeftAt = now;
            else p.LeftAt = null;
        }
        await _db.SaveChangesAsync();

        if (!seance.Animateur && req.Evenement != "present")
            await _tempsReel.VisioPresenceAsync(seance.AnimateurId, req.Type, req.Id, me, req.Evenement);

        return Ok(new { success = true, data = new { presenceId = p.Id } });
    }

    /// <summary>Appel de la séance (animateur seulement) : élèves attendus, présents maintenant, minutes passées.</summary>
    [HttpGet("presences")]
    public async Task<IActionResult> Presences([FromQuery] string type, [FromQuery] int id)
    {
        var me = User.GetUserId();
        var seance = TypeValide(type) ? await Charger(type, id, me) : null;
        if (seance == null || !seance.Animateur) return Introuvable();
        var now = DateTime.UtcNow;

        var passages = await _db.VisioPresences.AsNoTracking()
            .Where(x => x.Type == type && x.RefId == id && !x.IsHost)
            .Select(x => new { x.UserId, x.JoinedAt, x.LastSeenAt, x.LeftAt }).ToListAsync();
        var ids = seance.Participants.Union(passages.Select(x => x.UserId)).Distinct().ToList();
        var noms = await _db.Users.AsNoTracking().Where(u => ids.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username, u.AvatarUrl }).ToListAsync();

        var liste = ids.Select(uid =>
        {
            var siens = passages.Where(x => x.UserId == uid).ToList();
            var u = noms.FirstOrDefault(n => n.Id == uid);
            var present = siens.Any(x => x.LeftAt == null && now - x.LastSeenAt < SignalPerdu);
            // Chaque passage se termine à son départ, sinon maintenant si le signal est récent, sinon au dernier signal.
            var minutes = siens.Sum(x => ((x.LeftAt ?? (now - x.LastSeenAt < SignalPerdu ? now : x.LastSeenAt)) - x.JoinedAt).TotalMinutes);
            return new
            {
                userId = uid,
                nom = u == null ? $"Utilisateur {uid}" : NomAffiche(u.FirstName, u.LastName, u.Username, uid),
                avatarUrl = u?.AvatarUrl,
                attendu = seance.Participants.Contains(uid),
                present,
                venu = siens.Count > 0,
                premiereArrivee = siens.Count > 0 ? siens.Min(x => x.JoinedAt) : (DateTime?)null,
                minutes = (int)Math.Round(Math.Max(0, minutes)),
            };
        })
        .OrderByDescending(x => x.present).ThenByDescending(x => x.venu).ThenBy(x => x.nom)
        .ToList();

        return Ok(new { success = true, data = new { attendus = seance.Participants.Count, presents = liste.Count(x => x.present), venus = liste.Count(x => x.venu), liste } });
    }

    // ───────────────────────── Enregistrements ─────────────────────────

    private string BucketPrive => string.IsNullOrWhiteSpace(_config["Storage:PrivateBucket"]) ? _storage.Bucket : _config["Storage:PrivateBucket"]!;

    public sealed class DepotRequest
    {
        public string Type { get; set; } = "";
        public int Id { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "video/webm";
        public long SizeBytes { get; set; }
    }

    /// <summary>Prépare le dépôt d'un enregistrement (animateur) : URL S3 présignée d'envoi direct.</summary>
    [HttpPost("enregistrements/depot")]
    public async Task<IActionResult> PreparerDepot([FromBody] DepotRequest req)
    {
        var me = User.GetUserId();
        var seance = TypeValide(req.Type) ? await Charger(req.Type, req.Id, me) : null;
        if (seance == null || !seance.Animateur) return Introuvable();
        var contentType = (req.ContentType ?? "").Split(';')[0].Trim().ToLowerInvariant();
        if (!TypesVideo.Contains(contentType))
            return BadRequest(new { success = false, error = "Format non pris en charge : déposez une vidéo WebM ou MP4." });
        if (req.SizeBytes <= 0 || req.SizeBytes > TailleMaxEnregistrement)
            return BadRequest(new { success = false, error = "Fichier vide ou trop volumineux (4 Go au plus)." });
        if (!await _storage.IsS3ReadyAsync())
            return StatusCode(503, new { success = false, error = "Stockage indisponible pour l'instant. Réessayez dans quelques minutes." });

        var ext = contentType switch { "video/mp4" => ".mp4", "video/x-matroska" => ".mkv", _ => ".webm" };
        var cle = $"private/visio/{req.Type}-{req.Id}/{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}{ext}";
        using var s3 = _storage.CreateS3Client();
        var url = s3.GetPreSignedURL(new GetPreSignedUrlRequest
        {
            BucketName = BucketPrive,
            Key = cle,
            Verb = HttpVerb.PUT,
            ContentType = contentType,
            Expires = DateTime.UtcNow.AddHours(3),
        });
        return Ok(new { success = true, data = new { url, cle, contentType } });
    }

    public sealed class EnregistrementRequest
    {
        public string Type { get; set; } = "";
        public int Id { get; set; }
        public string Cle { get; set; } = "";
        public string FileName { get; set; } = "";
    }

    /// <summary>Enregistre un dépôt terminé (animateur) : vérifie l'objet dans S3, puis le rattache à la séance.</summary>
    [HttpPost("enregistrements")]
    public async Task<IActionResult> AjouterEnregistrement([FromBody] EnregistrementRequest req)
    {
        var me = User.GetUserId();
        var seance = TypeValide(req.Type) ? await Charger(req.Type, req.Id, me) : null;
        if (seance == null || !seance.Animateur) return Introuvable();
        if (!req.Cle.StartsWith($"private/visio/{req.Type}-{req.Id}/", StringComparison.Ordinal) || req.Cle.Contains(".."))
            return BadRequest(new { success = false, error = "Dépôt invalide." });

        GetObjectMetadataResponse meta;
        try
        {
            using var s3 = _storage.CreateS3Client();
            meta = await s3.GetObjectMetadataAsync(BucketPrive, req.Cle);
        }
        catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return BadRequest(new { success = false, error = "Le fichier n'est pas arrivé dans le stockage. Recommencez le dépôt." });
        }
        catch (AmazonS3Exception ex)
        {
            // Droits S3 du serveur insuffisants ou stockage indisponible : message clair plutôt qu'une erreur 500.
            _logger.LogWarning(ex, "Visio : vérification de l'enregistrement {Cle} impossible", req.Cle);
            return StatusCode(503, new { success = false, error = "Stockage indisponible pour l'instant. Réessayez dans quelques minutes." });
        }

        var nomFichier = Path.GetFileName(string.IsNullOrWhiteSpace(req.FileName) ? req.Cle : req.FileName);
        if (nomFichier.Length > 200) nomFichier = nomFichier[^200..];
        var e = new VisioEnregistrement
        {
            Type = req.Type, RefId = req.Id, UploadedByUserId = me, StorageKey = req.Cle, FileName = nomFichier,
            ContentType = meta.Headers.ContentType ?? "video/webm", SizeBytes = meta.ContentLength,
        };
        _db.VisioEnregistrements.Add(e);
        await _db.SaveChangesAsync();
        return Ok(new { success = true, data = new { e.Id } });
    }

    /// <summary>Enregistrements d'une séance (animateur et participants), avec un lien de lecture valable 6 h.</summary>
    [HttpGet("enregistrements")]
    public async Task<IActionResult> Enregistrements([FromQuery] string type, [FromQuery] int id)
    {
        var me = User.GetUserId();
        var seance = TypeValide(type) ? await Charger(type, id, me) : null;
        if (seance == null) return Introuvable();
        var liste = await _db.VisioEnregistrements.AsNoTracking()
            .Where(x => x.Type == type && x.RefId == id && !x.IsDeleted)
            .OrderBy(x => x.CreatedAt).ToListAsync();
        using var s3 = liste.Count > 0 ? _storage.CreateS3Client() : null;
        var data = liste.Select(x => new
        {
            x.Id, x.FileName, x.ContentType, x.SizeBytes, x.CreatedAt,
            url = s3!.GetPreSignedURL(new GetPreSignedUrlRequest
            {
                BucketName = BucketPrive, Key = x.StorageKey, Verb = HttpVerb.GET, Expires = DateTime.UtcNow.AddHours(6),
            }),
        }).ToList();
        return Ok(new { success = true, data = new { titre = seance.Titre, animateur = seance.Animateur, liste = data } });
    }

    /// <summary>Supprime un enregistrement (animateur), fichier compris.</summary>
    [HttpDelete("enregistrements/{enregistrementId:int}")]
    public async Task<IActionResult> SupprimerEnregistrement(int enregistrementId)
    {
        var me = User.GetUserId();
        var e = await _db.VisioEnregistrements.FirstOrDefaultAsync(x => x.Id == enregistrementId && !x.IsDeleted);
        if (e == null) return Introuvable();
        var seance = await Charger(e.Type, e.RefId, me);
        if (seance == null || !seance.Animateur) return Introuvable();
        e.IsDeleted = true;
        await _db.SaveChangesAsync();
        try
        {
            using var s3 = _storage.CreateS3Client();
            await s3.DeleteObjectAsync(BucketPrive, e.StorageKey);
        }
        catch (Exception ex) { _logger.LogWarning(ex, "Visio : fichier d'enregistrement {Cle} non supprimé", e.StorageKey); }
        return Ok(new { success = true });
    }

    // ───────────────────────── Mes cours en direct ─────────────────────────

    private string Etat(DateTime debut, DateTime fin, DateTime now) =>
        now < debut - OuvertureAvant ? "a_venir" : now > fin + MargeApres ? "terminee" : "ouverte";

    /// <summary>
    /// Cours en direct de l'utilisateur (30 jours passés, 90 à venir), tous rôles confondus : séances qu'il anime
    /// (professeur, répétiteur), séances qu'il suit (inscriptions, réservations en ligne), et pour un parent
    /// celles de ses enfants liés (consultation seulement : seul l'élève entre dans la salle).
    /// Même liste pour le site et l'application mobile.
    /// </summary>
    [HttpGet("mes-cours")]
    public async Task<IActionResult> MesCours()
    {
        var me = User.GetUserId();
        var now = DateTime.UtcNow;
        var depuis = now.AddDays(-30);
        var jusqua = now.AddDays(90);
        var dateDepuis = DateOnly.FromDateTime(depuis);
        var dateJusqua = DateOnly.FromDateTime(jusqua);

        var enfants = await _db.ParentStudentLinks.AsNoTracking()
            .Where(l => l.ParentId == me && l.Status == "accepted").Select(l => l.StudentId).ToListAsync();
        var eleves = enfants.Append(me).ToList();

        // Séances d'enseignement : animées par moi, ou suivies par moi ou mes enfants.
        var seances = await _db.Sessions.AsNoTracking()
            .Where(s => !s.IsDeleted && s.Type == "live" && s.Status != "cancelled" && s.EndDate >= depuis && s.StartDate <= jusqua
                && (s.CreatedBy == me || _db.SessionEnrollments.Any(e => e.SessionId == s.Id && eleves.Contains(e.StudentId) && e.PaymentStatus != "refunded")))
            .Select(s => new { s.Id, s.Title, s.Subject, s.StartDate, s.EndDate, s.CreatedBy })
            .ToListAsync();
        var idsSeances = seances.Select(s => s.Id).ToList();
        var inscriptions = await _db.SessionEnrollments.AsNoTracking()
            .Where(e => idsSeances.Contains(e.SessionId) && eleves.Contains(e.StudentId))
            .Select(e => new { e.SessionId, e.StudentId, e.PaymentStatus }).ToListAsync();

        // Tutorats en ligne : comme répétiteur, ou comme participant (moi ou mes enfants).
        var tutorats = await _db.TutorBookings.AsNoTracking()
            .Include(b => b.TutorProfile).Include(b => b.Participants)
            .Where(b => b.Mode == "online" && (b.Status == "confirmed" || b.Status == "completed" || b.Status == "disputed")
                && b.SessionDate >= dateDepuis && b.SessionDate <= dateJusqua
                && ((b.TutorProfile != null && b.TutorProfile.UserId == me) || eleves.Contains(b.StudentUserId)
                    || b.Participants.Any(p => eleves.Contains(p.StudentUserId))))
            .ToListAsync();

        var idsUtilisateurs = seances.Where(s => s.CreatedBy != null).Select(s => s.CreatedBy!.Value)
            .Concat(tutorats.Where(b => b.TutorProfile != null).Select(b => b.TutorProfile!.UserId))
            .Concat(enfants).Distinct().ToList();
        var noms = await _db.Users.AsNoTracking().Where(u => idsUtilisateurs.Contains(u.Id))
            .Select(u => new { u.Id, u.FirstName, u.LastName, u.Username }).ToListAsync();
        string Nom(int? id) { var u = noms.FirstOrDefault(n => n.Id == id); return u == null ? "" : NomAffiche(u.FirstName, u.LastName, u.Username, u.Id); }

        var nbEnreg = await _db.VisioEnregistrements.AsNoTracking()
            .Where(x => !x.IsDeleted && ((x.Type == "seance" && idsSeances.Contains(x.RefId)) || x.Type == "tutorat"))
            .GroupBy(x => new { x.Type, x.RefId }).Select(g => new { g.Key.Type, g.Key.RefId, N = g.Count() }).ToListAsync();
        int Enreg(string type, int id) => nbEnreg.FirstOrDefault(x => x.Type == type && x.RefId == id)?.N ?? 0;

        var liste = new List<object>();
        foreach (var s in seances)
        {
            var debut = DateTime.SpecifyKind(s.StartDate, DateTimeKind.Utc);
            var fin = DateTime.SpecifyKind(s.EndDate, DateTimeKind.Utc);
            var mienne = inscriptions.FirstOrDefault(e => e.SessionId == s.Id && e.StudentId == me);
            var enfant = inscriptions.FirstOrDefault(e => e.SessionId == s.Id && enfants.Contains(e.StudentId));
            var role = s.CreatedBy == me ? "animateur" : mienne != null ? "participant" : "enfant";
            var paiement = role == "participant" ? mienne!.PaymentStatus : role == "enfant" ? enfant?.PaymentStatus : null;
            liste.Add(new
            {
                type = "seance", id = s.Id, titre = s.Title, matiere = s.Subject, debut, fin, role,
                animateur = Nom(s.CreatedBy), enfant = role == "enfant" ? Nom(enfant?.StudentId) : null,
                enAttentePaiement = paiement == "pending",
                etat = Etat(debut, fin, now), enregistrements = Enreg("seance", s.Id),
            });
        }
        foreach (var b in tutorats)
        {
            var debut = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.StartTime), DateTimeKind.Utc);
            var fin = b.SessionDate.ToDateTime(TimeOnly.FromTimeSpan(b.EndTime), DateTimeKind.Utc);
            var participants = b.Participants.Select(p => p.StudentUserId).Append(b.StudentUserId).ToList();
            var role = b.TutorProfile?.UserId == me ? "animateur" : participants.Contains(me) ? "participant" : "enfant";
            liste.Add(new
            {
                type = "tutorat", id = b.Id, titre = string.IsNullOrWhiteSpace(b.Subject) ? "Séance de tutorat" : $"Tutorat : {b.Subject}",
                matiere = b.Subject, debut, fin, role,
                animateur = Nom(b.TutorProfile?.UserId), enfant = role == "enfant" ? Nom(participants.FirstOrDefault(enfants.Contains)) : null,
                enAttentePaiement = false,
                etat = b.Status == "confirmed" ? Etat(debut, fin, now) : "terminee", enregistrements = Enreg("tutorat", b.Id),
            });
        }

        var tries = liste.Cast<dynamic>()
            .OrderBy(x => x.etat == "ouverte" ? 0 : x.etat == "a_venir" ? 1 : 2)
            .ThenBy(x => x.etat == "terminee" ? -((DateTime)x.debut).Ticks : ((DateTime)x.debut).Ticks)
            .ToList();
        return Ok(new { success = true, data = tries });
    }

    /// <summary>
    /// Fiche d'une séance d'enseignement en direct pour tout utilisateur connecté (lien partagé /sessions/:id) :
    /// de quoi s'inscrire puis entrer dans la salle. Ne renvoie ni recettes ni transcription.
    /// </summary>
    [HttpGet("seance/{id:int}")]
    public async Task<IActionResult> FicheSeance(int id)
    {
        var me = User.GetUserId();
        var s = await _db.Sessions.AsNoTracking()
            .Where(x => x.Id == id && !x.IsDeleted && x.Type == "live")
            .Select(x => new { x.Id, x.Title, x.Description, x.Subject, x.Level, x.StartDate, x.EndDate, x.DurationMinutes,
                x.MaxParticipants, x.IsFree, x.PriceXaf, x.Status, x.CreatedBy, x.ExternalLink })
            .FirstOrDefaultAsync();
        if (s == null) return Introuvable();
        var inscrits = await _db.SessionEnrollments.AsNoTracking().CountAsync(e => e.SessionId == id && e.PaymentStatus != "refunded");
        var mienne = await _db.SessionEnrollments.AsNoTracking().Where(e => e.SessionId == id && e.StudentId == me)
            .Select(e => e.PaymentStatus).FirstOrDefaultAsync();
        var prof = await _db.Users.AsNoTracking().Where(u => u.Id == s.CreatedBy)
            .Select(u => new { u.FirstName, u.LastName, u.Username, u.AvatarUrl }).FirstOrDefaultAsync();
        var debut = DateTime.SpecifyKind(s.StartDate, DateTimeKind.Utc);
        var fin = DateTime.SpecifyKind(s.EndDate, DateTimeKind.Utc);
        var monRole = s.CreatedBy == me ? "animateur"
            : mienne is "free" or "paid" ? "inscrit"
            : mienne == "pending" ? "paiement_en_attente"
            : "aucun";
        var gratuit = s.IsFree || s.PriceXaf is not > 0;
        return Ok(new
        {
            success = true,
            data = new
            {
                s.Id, titre = s.Title, description = s.Description, matiere = s.Subject, niveau = s.Level, debut, fin,
                dureeMinutes = s.DurationMinutes, gratuit, prixXaf = gratuit ? null : s.PriceXaf,
                placesRestantes = s.MaxParticipants.HasValue ? Math.Max(0, s.MaxParticipants.Value - inscrits) : (int?)null,
                inscrits, statut = s.Status, annulee = s.Status == "cancelled",
                lienExterne = monRole is "animateur" or "inscrit" ? s.ExternalLink : null,
                professeur = prof == null ? null : new { nom = NomAffiche(prof.FirstName, prof.LastName, prof.Username, s.CreatedBy ?? 0), avatarUrl = prof.AvatarUrl },
                monRole,
                etat = s.Status == "cancelled" ? "annulee" : Etat(debut, fin, DateTime.UtcNow),
            },
        });
    }

    // ───────────────────────── Signature du jeton ─────────────────────────

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
