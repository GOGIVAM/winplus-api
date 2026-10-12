using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Fichier d'identité reçu (recto, verso ou selfie).</summary>
public sealed record TutorIdentityUpload(Stream Content, string FileName, string? ContentType, long Length);

public interface ITutorVerificationService
{
    // ── Tuteur ──
    Task<TutorVerificationStatusDto> GetStatusForTutorAsync(int tutorUserId);
    Task<TutorVerificationStatusDto> SubmitIdentityAsync(int tutorUserId, string documentType, TutorIdentityUpload front, TutorIdentityUpload? back, TutorIdentityUpload selfie, int? interviewId = null);
    Task<TutorVerificationStatusDto> RequestInterviewAsync(int tutorUserId, RequestTutorInterviewDto request);
    Task<TutorVerificationStatusDto> CancelInterviewAsync(int tutorUserId, int interviewId);

    // ── Institution ──
    Task<List<InstitutionTutorForEndorsementDto>> GetInstitutionTutorsAsync(int institutionUserId);
    Task<TutorEndorsementDto> EndorseAsync(int institutionUserId, CreateTutorEndorsementDto request);
    Task<TutorEndorsementDto> RevokeEndorsementAsync(int actorUserId, int endorsementId, string? reason, bool asAdmin = false);
    /// <summary>Rattachement tuteur-institution supprimé ou refusé : la caution de cette institution tombe.</summary>
    Task OnAffiliationEndedAsync(int institutionId, int teacherUserId, int actorUserId, string actorRole);

    // ── Admin : pièce d'identité ──
    Task<List<AdminIdentityCheckDto>> GetIdentityChecksAsync(string? status);
    Task<AdminIdentityFilesDto> GetIdentityFilesAsync(int checkId, string apiBaseForLocal);
    Task<(Stream Content, string ContentType)?> OpenIdentityFileAsync(int checkId, string kind);
    Task<AdminIdentityCheckDto> ReviewIdentityAsync(int adminUserId, int checkId, bool approve, string? reason);
    Task<AdminIdentityCheckDto> SubmitIdentityForInterviewAsync(int adminUserId, int interviewId, string documentType, TutorIdentityUpload front, TutorIdentityUpload? back, TutorIdentityUpload selfie);

    // ── Admin : entretien ──
    Task<List<AdminInterviewDto>> GetInterviewsAsync(string? status);
    Task<AdminInterviewDto> GetInterviewAsync(int interviewId);
    Task<AdminInterviewDto> ScheduleInterviewAsync(int adminUserId, int interviewId, ScheduleInterviewDto request);
    Task<AdminInterviewDto> SaveInterviewNotesAsync(int adminUserId, int interviewId, SaveInterviewNotesDto request);
    Task<AdminInterviewDto> SaveInterviewAiAsync(int interviewId, string? guideJson, string? reportDraft);
    Task<AdminInterviewDto> CompleteInterviewAsync(int adminUserId, int interviewId, CompleteInterviewDto request);

    // ── Admin : contrôle, historique ──
    Task<AdminInPersonControlDto> GetInPersonControlAsync();
    Task<List<TutorVerificationEventDto>> GetHistoryAsync(int tutorProfileId, bool forTutor);

    // ── Statut calculé ──
    Task<bool> RecomputeAsync(int tutorProfileId, int? actorUserId, string actorRole, string? reason);
    /// <summary>Tâche de fond : recalcule les tuteurs vérifiés par caution (rattachements modifiés ailleurs).</summary>
    Task<int> RecomputeInstitutionRoutesAsync();
    /// <summary>Tâche de fond : suppression des fichiers d'identité selon la règle de conservation.</summary>
    Task<int> PurgeExpiredIdentityFilesAsync(DateTime nowUtc);
}

/// <summary>
/// Vérification des tuteurs pour le présentiel (lot 9.5, suivi 17.11 et 17.16).
///
/// Statut calculé (<see cref="RecomputeAsync"/>) à partir des voies valides :
/// <list type="bullet">
///   <item><b>identity</b> : une pièce d'identité + selfie validée par un admin ;</item>
///   <item><b>interview</b> : un entretien réussi ET une pièce d'identité + selfie
///   validée (17.16 : l'entretien remplace le diplôme, pas l'identité ; un
///   entretien réussi sans identité validée ne vérifie pas) ;</item>
///   <item><b>institution</b> : une caution active d'une institution à laquelle
///   le tuteur est TOUJOURS rattaché (InstitutionTeacherLink accepté). Révoquer
///   la caution ou le rattachement retire cette voie, pas les autres.</item>
/// </list>
/// Le diplôme (IsDiplomaVerified) reste un badge distinct, sans effet ici.
///
/// Fichiers d'identité : stockage privé (<see cref="IPrivateFileStore"/>), lus
/// par un admin seulement (URL présignée de 5 minutes), jamais exposés au
/// public, au tuteur après dépôt, ni à d'autres utilisateurs. Conservation
/// (RGPD, voir <see cref="TutorVerificationSettings"/>) : dépôt remplacé avant
/// décision supprimé tout de suite ; refusé : 7 jours ; validé : 30 jours.
/// La décision (qui, quand, motif) reste dans l'historique.
/// </summary>
public class TutorVerificationService : ITutorVerificationService
{
    private static readonly string[] AllowedImageExtensions = { ".jpg", ".jpeg", ".png", ".webp" };
    private static readonly string[] AllowedDocumentExtensions = { ".jpg", ".jpeg", ".png", ".webp", ".pdf" };
    private static readonly string[] DocumentTypes = { "cni", "passport", "residence_permit", "driving_license", "other" };
    public const long MaxIdentityFileBytes = 8 * 1024 * 1024;
    private static readonly string[] UpcomingStatuses = { "pending_payment", "pending_tutor_approval", "confirmed" };

    private readonly ApplicationDbContext _db;
    private readonly IPrivateFileStore _files;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorVerificationService> _logger;
    private readonly TutorVerificationSettings _settings;

    public TutorVerificationService(ApplicationDbContext db, IPrivateFileStore files, INtfyService ntfy,
        ILogger<TutorVerificationService> logger, IConfiguration configuration)
    {
        _db = db;
        _files = files;
        _ntfy = ntfy;
        _logger = logger;
        _settings = TutorVerificationSettings.From(configuration);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Règles partagées
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Cautions qui comptent : actives ET rattachement institution-tuteur toujours accepté.</summary>
    public static IQueryable<TutorInstitutionEndorsement> ValidEndorsements(ApplicationDbContext db) =>
        db.TutorInstitutionEndorsements.Where(e => e.Status == TutorEndorsementStatuses.Active
            && db.InstitutionTeacherLinks.Any(l => l.InstitutionId == e.InstitutionId
                                                 && l.TeacherId == e.TutorProfile!.UserId
                                                 && l.Status == "accepted"));

    /// <summary>Noms des institutions qui cautionnent chaque profil (badges publics).</summary>
    public static async Task<Dictionary<int, List<string>>> EndorsingInstitutionNamesAsync(ApplicationDbContext db, List<int> profileIds)
    {
        if (profileIds.Count == 0) return new Dictionary<int, List<string>>();
        var rows = await ValidEndorsements(db).AsNoTracking()
            .Where(e => profileIds.Contains(e.TutorProfileId))
            .Select(e => new { e.TutorProfileId, Name = e.Institution!.Name })
            .ToListAsync();
        return rows.GroupBy(r => r.TutorProfileId)
            .ToDictionary(g => g.Key, g => g.Select(r => r.Name).Distinct().OrderBy(n => n).ToList());
    }

    private static string? DisplayName(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email.Split('@')[0]);

    private static string Truncate(string? value, int max) =>
        string.IsNullOrEmpty(value) ? "" : value.Length <= max ? value : value[..max];

    private void AddEvent(int profileId, string category, string action, int? actorUserId, string actorRole, string? reason = null, int? relatedId = null) =>
        _db.TutorVerificationEvents.Add(new TutorVerificationEvent
        {
            TutorProfileId = profileId,
            Category = category,
            Action = action,
            ActorUserId = actorUserId,
            ActorRole = actorRole,
            Reason = string.IsNullOrWhiteSpace(reason) ? null : Truncate(reason, 500),
            RelatedId = relatedId,
        });

    private async Task<TutorProfile> TutorProfileOfAsync(int tutorUserId) =>
        await _db.TutorProfiles.FirstOrDefaultAsync(p => p.UserId == tutorUserId)
        ?? throw new InvalidOperationException("Active d'abord ton profil répétiteur pour demander une vérification.");

    private Task NotifyTutorAsync(int tutorUserId, string title, string message, string? entityType = null, int? entityId = null) =>
        _ntfy.PublishAsync($"winplus-user-{tutorUserId}", title, message, userId: tutorUserId, type: "TutorVerification",
            relatedEntityType: entityType, relatedEntityId: entityId);

    // ═════════════════════════════════════════════════════════════════════
    //  Statut calculé
    // ═════════════════════════════════════════════════════════════════════

    public async Task<bool> RecomputeAsync(int tutorProfileId, int? actorUserId, string actorRole, string? reason)
    {
        var profile = await _db.TutorProfiles.FirstOrDefaultAsync(p => p.Id == tutorProfileId);
        if (profile == null) return false;

        var hasIdentity = await _db.TutorIdentityChecks.AnyAsync(c => c.TutorProfileId == tutorProfileId && c.Status == TutorIdentityStatuses.Approved);
        var hasInterview = await _db.TutorVerificationInterviews.AnyAsync(i => i.TutorProfileId == tutorProfileId && i.Status == TutorInterviewStatuses.Passed);
        var hasEndorsement = await ValidEndorsements(_db).AnyAsync(e => e.TutorProfileId == tutorProfileId);

        var routes = new List<string>();
        if (hasIdentity) routes.Add(TutorVerificationRoutes.Identity);
        if (hasInterview && hasIdentity) routes.Add(TutorVerificationRoutes.Interview);
        if (hasEndorsement) routes.Add(TutorVerificationRoutes.Institution);
        var verified = routes.Count > 0;
        var wasVerified = profile.InPersonVerifiedAt != null;

        profile.InPersonVerificationRoutes = verified ? string.Join(',', routes) : null;
        if (verified && !wasVerified)
        {
            profile.InPersonVerifiedAt = DateTime.UtcNow;
            AddEvent(profile.Id, "status", "verified", actorUserId, actorRole, reason ?? $"Voie(s) : {string.Join(", ", routes)}");
        }
        else if (!verified && wasVerified)
        {
            profile.InPersonVerifiedAt = null;
            AddEvent(profile.Id, "status", "unverified", actorUserId, actorRole, reason);
        }
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        if (verified && !wasVerified)
        {
            await NotifyTutorAsync(profile.UserId, "Vérifié pour le présentiel",
                TutorInPersonPolicy.IsSuspended(profile)
                    ? "Ton profil est vérifié, mais ton présentiel reste suspendu jusqu'à l'examen en cours par l'équipe WinPlus."
                    : "Ton profil est vérifié : tu peux maintenant proposer des séances à domicile, chez toi ou en lieu neutre depuis tes réglages.");
        }
        else if (!verified && wasVerified)
        {
            var upcoming = await UpcomingInPersonQuery(_db).CountAsync(b => b.TutorProfileId == profile.Id);
            await NotifyTutorAsync(profile.UserId, "Vérification pour le présentiel retirée",
                $"{reason ?? "Ta vérification n'est plus valide."} Tes séances en ligne restent possibles ; tu peux te faire vérifier par une autre voie.");
            await _ntfy.PublishAdminAsync("Répétiteur plus vérifié pour le présentiel",
                $"Profil répétiteur #{profile.Id} : vérification retirée ({reason ?? "voie retirée"}). {upcoming} séance(s) présentielle(s) à venir déjà réservée(s), non annulée(s).",
                priority: "default", tags: new[] { "warning" }, relatedEntityType: "TutorProfile", relatedEntityId: profile.Id);
        }
        return verified;
    }

    public async Task<int> RecomputeInstitutionRoutesAsync()
    {
        // Profils dont la voie « institution » est en cache, ou qui ont une
        // caution active : un rattachement supprimé ailleurs (compte supprimé,
        // institution désactivée) est rattrapé ici.
        var ids = await _db.TutorProfiles.AsNoTracking()
            .Where(p => (p.InPersonVerificationRoutes != null && p.InPersonVerificationRoutes.Contains(TutorVerificationRoutes.Institution))
                        || _db.TutorInstitutionEndorsements.Any(e => e.TutorProfileId == p.Id && e.Status == TutorEndorsementStatuses.Active))
            .Select(p => p.Id).ToListAsync();
        var changed = 0;
        foreach (var id in ids)
        {
            var before = await _db.TutorProfiles.AsNoTracking().Where(p => p.Id == id).Select(p => p.InPersonVerifiedAt != null).FirstAsync();
            var after = await RecomputeAsync(id, null, "system", "Rattachement à l'institution qui cautionnait le tuteur introuvable.");
            if (before != after) changed++;
        }
        return changed;
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Tuteur
    // ═════════════════════════════════════════════════════════════════════

    public async Task<TutorVerificationStatusDto> GetStatusForTutorAsync(int tutorUserId)
    {
        var profile = await _db.TutorProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == tutorUserId)
            ?? throw new InvalidOperationException("Active d'abord ton profil répétiteur pour demander une vérification.");
        return await BuildStatusAsync(profile);
    }

    private async Task<TutorVerificationStatusDto> BuildStatusAsync(TutorProfile profile)
    {
        var latestIdentity = await _db.TutorIdentityChecks.AsNoTracking()
            .Where(c => c.TutorProfileId == profile.Id && c.Status != TutorIdentityStatuses.Superseded)
            .OrderByDescending(c => c.SubmittedAt).FirstOrDefaultAsync();
        var hasApproved = await _db.TutorIdentityChecks.AnyAsync(c => c.TutorProfileId == profile.Id && c.Status == TutorIdentityStatuses.Approved);
        var latestInterview = await _db.TutorVerificationInterviews.AsNoTracking()
            .Where(i => i.TutorProfileId == profile.Id)
            .OrderByDescending(i => i.RequestedAt).FirstOrDefaultAsync();
        var interviewIdentity = latestInterview == null ? false
            : await _db.TutorIdentityChecks.AnyAsync(c => c.TutorVerificationInterviewId == latestInterview.Id && c.Status != TutorIdentityStatuses.Superseded);
        var hasPassed = await _db.TutorVerificationInterviews.AnyAsync(i => i.TutorProfileId == profile.Id && i.Status == TutorInterviewStatuses.Passed);

        return new TutorVerificationStatusDto
        {
            TutorProfileId = profile.Id,
            IsVerified = TutorInPersonPolicy.IsVerified(profile),
            VerifiedAt = profile.InPersonVerifiedAt,
            Routes = string.IsNullOrEmpty(profile.InPersonVerificationRoutes) ? new List<string>() : profile.InPersonVerificationRoutes.Split(',').ToList(),
            IsSuspended = TutorInPersonPolicy.IsSuspended(profile),
            SuspendedAt = profile.InPersonSuspendedAt,
            SuspensionReason = profile.InPersonSuspensionReason,
            InPersonAllowed = TutorInPersonPolicy.IsEligible(profile),
            IsDiplomaVerified = profile.IsDiplomaVerified,
            LatestIdentity = latestIdentity == null ? null : IdentitySummary(latestIdentity),
            HasApprovedIdentity = hasApproved,
            LatestInterview = latestInterview == null ? null : InterviewSummary(latestInterview, interviewIdentity),
            HasPassedInterview = hasPassed,
            Endorsements = await EndorsementsForProfileAsync(profile.Id, includeRevoked: true),
            History = await GetHistoryAsync(profile.Id, forTutor: true),
            IdentityRetentionDaysAfterApproval = _settings.IdentityRetentionDaysAfterApproval,
            IdentityRetentionDaysAfterRejection = _settings.IdentityRetentionDaysAfterRejection,
        };
    }

    private static TutorIdentityCheckSummaryDto IdentitySummary(TutorIdentityCheck c) => new()
    {
        Id = c.Id,
        Source = c.Source,
        DocumentType = c.DocumentType,
        HasBack = c.BackFileRef != null,
        Status = c.Status,
        RejectionReason = c.RejectionReason,
        SubmittedAt = c.SubmittedAt,
        ReviewedAt = c.ReviewedAt,
        FilesPurgedAt = c.FilesPurgedAt,
        InterviewId = c.TutorVerificationInterviewId,
    };

    private static TutorInterviewSummaryDto InterviewSummary(TutorVerificationInterview i, bool hasIdentity) => new()
    {
        Id = i.Id,
        Format = i.Format,
        Status = i.Status,
        Availability = i.Availability,
        City = i.City,
        TutorMessage = i.TutorMessage,
        ScheduledAt = i.ScheduledAt,
        MeetingUrl = i.Status == TutorInterviewStatuses.Scheduled ? i.MeetingUrl : null,
        Location = i.Status == TutorInterviewStatuses.Scheduled ? i.Location : null,
        OutcomeReason = i.OutcomeReason,
        RequestedAt = i.RequestedAt,
        CompletedAt = i.CompletedAt,
        HasIdentityAttached = hasIdentity,
    };

    /// <summary>Contrôle d'un fichier d'identité : taille, extension, signature réelle du contenu.</summary>
    private static async Task<(MemoryStream Buffer, string Extension, string ContentType)> ReadValidatedAsync(TutorIdentityUpload file, string label, bool imageOnly)
    {
        if (file.Length <= 0)
            throw new InvalidOperationException($"{label} : fichier manquant.");
        if (file.Length > MaxIdentityFileBytes)
            throw new InvalidOperationException($"{label} : fichier trop volumineux (8 Mo maximum).");
        var ext = Path.GetExtension(file.FileName ?? "").ToLowerInvariant();
        var allowed = imageOnly ? AllowedImageExtensions : AllowedDocumentExtensions;
        if (!allowed.Contains(ext))
            throw new InvalidOperationException(imageOnly
                ? $"{label} : photo attendue (JPG, PNG ou WEBP)."
                : $"{label} : format non supporté (JPG, PNG, WEBP ou PDF).");

        var buffer = new MemoryStream();
        await file.Content.CopyToAsync(buffer);
        if (buffer.Length > MaxIdentityFileBytes)
            throw new InvalidOperationException($"{label} : fichier trop volumineux (8 Mo maximum).");
        var head = buffer.GetBuffer().AsSpan(0, (int)Math.Min(12, buffer.Length)).ToArray();
        var kind = SniffKind(head);
        var ok = kind switch
        {
            "jpg" => ext is ".jpg" or ".jpeg",
            "png" => ext == ".png",
            "webp" => ext == ".webp",
            "pdf" => ext == ".pdf" && !imageOnly,
            _ => false,
        };
        if (!ok)
            throw new InvalidOperationException($"{label} : le contenu du fichier ne correspond pas à une image ou à un PDF valide.");
        buffer.Position = 0;
        return (buffer, ext == ".jpeg" ? ".jpg" : ext, PrivateFileStore.ContentTypeOf("x" + ext));
    }

    private static string? SniffKind(byte[] h)
    {
        if (h.Length >= 3 && h[0] == 0xFF && h[1] == 0xD8 && h[2] == 0xFF) return "jpg";
        if (h.Length >= 4 && h[0] == 0x89 && h[1] == 0x50 && h[2] == 0x4E && h[3] == 0x47) return "png";
        if (h.Length >= 12 && h[0] == 'R' && h[1] == 'I' && h[2] == 'F' && h[3] == 'F' && h[8] == 'W' && h[9] == 'E' && h[10] == 'B' && h[11] == 'P') return "webp";
        if (h.Length >= 4 && h[0] == '%' && h[1] == 'P' && h[2] == 'D' && h[3] == 'F') return "pdf";
        return null;
    }

    /// <summary>Dépôt commun (tuteur ou agent) : fichiers privés, ancienne demande en attente remplacée.</summary>
    private async Task<TutorIdentityCheck> CreateIdentityCheckAsync(TutorProfile profile, int submittedBy, string actorRole, string source, int? interviewId,
        string documentType, TutorIdentityUpload front, TutorIdentityUpload? back, TutorIdentityUpload selfie)
    {
        documentType = (documentType ?? "").Trim().ToLowerInvariant();
        if (!DocumentTypes.Contains(documentType))
            throw new InvalidOperationException("Type de pièce invalide (carte d'identité, passeport, titre de séjour, permis de conduire ou autre).");

        var f = await ReadValidatedAsync(front, "Recto de la pièce", imageOnly: false);
        var b = back == null ? ((MemoryStream, string, string)?)null : await ReadValidatedAsync(back, "Verso de la pièce", imageOnly: false);
        var s = await ReadValidatedAsync(selfie, "Selfie", imageOnly: true);

        // Clés imprévisibles, sous private/ (jamais d'URL publique).
        var folder = $"private/tutor-identity/{profile.Id}/{Guid.NewGuid():N}";
        var stored = new List<string>();
        try
        {
            var frontRef = await _files.PutAsync(f.Buffer, $"{folder}/front{f.Extension}", f.ContentType);
            stored.Add(frontRef);
            string? backRef = null;
            if (b is { } bb)
            {
                backRef = await _files.PutAsync(bb.Item1, $"{folder}/back{bb.Item2}", bb.Item3);
                stored.Add(backRef);
            }
            var selfieRef = await _files.PutAsync(s.Buffer, $"{folder}/selfie{s.Extension}", s.ContentType);
            stored.Add(selfieRef);

            // Un dépôt en attente plus ancien est remplacé : ses fichiers sont supprimés tout de suite.
            var olderPending = await _db.TutorIdentityChecks
                .Where(c => c.TutorProfileId == profile.Id && c.Status == TutorIdentityStatuses.Pending)
                .ToListAsync();
            foreach (var old in olderPending)
            {
                old.Status = TutorIdentityStatuses.Superseded;
                await PurgeFilesAsync(old);
                AddEvent(profile.Id, "identity", "superseded", submittedBy, actorRole, "Remplacé par un nouveau dépôt.", old.Id);
            }

            var check = new TutorIdentityCheck
            {
                TutorProfileId = profile.Id,
                Source = source,
                TutorVerificationInterviewId = interviewId,
                DocumentType = documentType,
                FrontFileRef = frontRef,
                BackFileRef = backRef,
                SelfieFileRef = selfieRef,
                Status = TutorIdentityStatuses.Pending,
                SubmittedByUserId = submittedBy,
            };
            _db.TutorIdentityChecks.Add(check);
            await _db.SaveChangesAsync();
            AddEvent(profile.Id, "identity", "submitted", submittedBy, actorRole,
                source == TutorIdentitySources.Interview ? $"Jointe à l'entretien #{interviewId}." : null, check.Id);
            await _db.SaveChangesAsync();
            return check;
        }
        catch
        {
            // Rien ne doit rester dans le stockage sans ligne qui le référence.
            foreach (var r in stored) await _files.DeleteAsync(r);
            throw;
        }
        finally
        {
            f.Buffer.Dispose();
            s.Buffer.Dispose();
            if (b is { } bb2) bb2.Item1.Dispose();
        }
    }

    public async Task<TutorVerificationStatusDto> SubmitIdentityAsync(int tutorUserId, string documentType, TutorIdentityUpload front, TutorIdentityUpload? back, TutorIdentityUpload selfie, int? interviewId = null)
    {
        var profile = await TutorProfileOfAsync(tutorUserId);
        if (await _db.TutorIdentityChecks.AnyAsync(c => c.TutorProfileId == profile.Id && c.Status == TutorIdentityStatuses.Approved))
            throw new InvalidOperationException("Ta pièce d'identité est déjà validée : aucun nouveau dépôt n'est nécessaire.");

        string source = TutorIdentitySources.Tutor;
        if (interviewId != null)
        {
            var interview = await _db.TutorVerificationInterviews.AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == interviewId && i.TutorProfileId == profile.Id)
                ?? throw new InvalidOperationException("Entretien introuvable.");
            if (!TutorInterviewStatuses.Open.Contains(interview.Status))
                throw new InvalidOperationException("Cet entretien est terminé.");
            source = TutorIdentitySources.Interview;
        }

        var check = await CreateIdentityCheckAsync(profile, tutorUserId, "tutor", source, interviewId, documentType, front, back, selfie);
        await _ntfy.PublishAdminAsync("Pièce d'identité à vérifier",
            $"Un répétiteur (profil #{profile.Id}) a déposé une pièce d'identité et un selfie à comparer.",
            priority: "default", tags: new[] { "identification_card" }, relatedEntityType: "TutorIdentityCheck", relatedEntityId: check.Id);
        return await BuildStatusAsync(profile);
    }

    public async Task<TutorVerificationStatusDto> RequestInterviewAsync(int tutorUserId, RequestTutorInterviewDto request)
    {
        var profile = await TutorProfileOfAsync(tutorUserId);
        var format = (request.Format ?? "").Trim().ToLowerInvariant();
        if (format is not (TutorInterviewFormats.Video or TutorInterviewFormats.InPerson))
            throw new InvalidOperationException("Choisis la forme de l'entretien : visio ou rencontre.");
        if (string.IsNullOrWhiteSpace(request.Availability) || request.Availability.Trim().Length < 5)
            throw new InvalidOperationException("Indique tes disponibilités (jours et heures).");
        if (format == TutorInterviewFormats.InPerson && string.IsNullOrWhiteSpace(request.City))
            throw new InvalidOperationException("Indique la ville de la rencontre.");
        if (await _db.TutorVerificationInterviews.AnyAsync(i => i.TutorProfileId == profile.Id && TutorInterviewStatuses.Open.Contains(i.Status)))
            throw new InvalidOperationException("Tu as déjà une demande d'entretien en cours.");

        var interview = new TutorVerificationInterview
        {
            TutorProfileId = profile.Id,
            Format = format,
            Availability = Truncate(request.Availability!.Trim(), 500),
            City = format == TutorInterviewFormats.InPerson ? Truncate(request.City!.Trim(), 100) : null,
            TutorMessage = string.IsNullOrWhiteSpace(request.Message) ? null : Truncate(request.Message.Trim(), 500),
        };
        _db.TutorVerificationInterviews.Add(interview);
        await _db.SaveChangesAsync();
        AddEvent(profile.Id, "interview", "requested", tutorUserId, "tutor", format == TutorInterviewFormats.Video ? "Visio" : $"Rencontre à {interview.City}", interview.Id);
        await _db.SaveChangesAsync();

        await _ntfy.PublishAdminAsync("Demande d'entretien de vérification",
            $"Profil répétiteur #{profile.Id} : entretien {(format == TutorInterviewFormats.Video ? "en visio" : $"en rencontre ({interview.City})")} demandé. Disponibilités : {interview.Availability}",
            priority: "default", tags: new[] { "calendar" }, relatedEntityType: "TutorVerificationInterview", relatedEntityId: interview.Id);
        return await BuildStatusAsync(profile);
    }

    public async Task<TutorVerificationStatusDto> CancelInterviewAsync(int tutorUserId, int interviewId)
    {
        var profile = await TutorProfileOfAsync(tutorUserId);
        var interview = await _db.TutorVerificationInterviews.FirstOrDefaultAsync(i => i.Id == interviewId && i.TutorProfileId == profile.Id)
            ?? throw new InvalidOperationException("Entretien introuvable.");
        if (!TutorInterviewStatuses.Open.Contains(interview.Status))
            throw new InvalidOperationException("Cet entretien est déjà terminé.");
        interview.Status = TutorInterviewStatuses.Cancelled;
        interview.OutcomeReason = "Annulé par le répétiteur.";
        interview.CompletedAt = DateTime.UtcNow;
        interview.UpdatedAt = DateTime.UtcNow;
        AddEvent(profile.Id, "interview", "cancelled", tutorUserId, "tutor", "Annulé par le répétiteur.", interview.Id);
        await _db.SaveChangesAsync();
        return await BuildStatusAsync(profile);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Institution : caution
    // ═════════════════════════════════════════════════════════════════════

    private async Task<int> InstitutionOfAsync(int institutionUserId)
    {
        var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == institutionUserId)
            ?? throw new InvalidOperationException("Compte introuvable.");
        if (!string.Equals(user.Role, "institution", StringComparison.OrdinalIgnoreCase) || user.InstitutionId == null)
            throw new UnauthorizedAccessException("Réservé aux comptes institution rattachés à un établissement.");
        return user.InstitutionId.Value;
    }

    private async Task<List<TutorEndorsementDto>> EndorsementsForProfileAsync(int profileId, bool includeRevoked)
    {
        var query = _db.TutorInstitutionEndorsements.AsNoTracking()
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .Where(e => e.TutorProfileId == profileId);
        if (!includeRevoked) query = query.Where(e => e.Status == TutorEndorsementStatuses.Active);
        var rows = await query.OrderByDescending(e => e.CreatedAt).ToListAsync();
        var validIds = await ValidEndorsements(_db).Where(e => e.TutorProfileId == profileId).Select(e => e.Id).ToListAsync();
        return rows.Select(e => EndorsementDto(e, validIds.Contains(e.Id))).ToList();
    }

    private static TutorEndorsementDto EndorsementDto(TutorInstitutionEndorsement e, bool valid) => new()
    {
        Id = e.Id,
        TutorProfileId = e.TutorProfileId,
        TutorUserId = e.TutorProfile?.UserId ?? 0,
        TutorName = DisplayName(e.TutorProfile?.User),
        InstitutionId = e.InstitutionId,
        InstitutionName = e.Institution?.Name,
        Statement = e.Statement,
        VerifierName = e.VerifierName,
        VerifiedOn = e.VerifiedOn,
        Status = e.Status,
        IsValid = valid,
        CreatedAt = e.CreatedAt,
        RevokedAt = e.RevokedAt,
        RevocationReason = e.RevocationReason,
    };

    public async Task<List<InstitutionTutorForEndorsementDto>> GetInstitutionTutorsAsync(int institutionUserId)
    {
        var institutionId = await InstitutionOfAsync(institutionUserId);
        var teachers = await _db.InstitutionTeacherLinks.AsNoTracking()
            .Where(l => l.InstitutionId == institutionId && l.Status == "accepted")
            .Select(l => l.Teacher!)
            .ToListAsync();
        var teacherIds = teachers.Select(t => t.Id).ToList();
        var profiles = await _db.TutorProfiles.AsNoTracking().Where(p => teacherIds.Contains(p.UserId)).ToListAsync();
        var profileIds = profiles.Select(p => p.Id).ToList();
        var active = await _db.TutorInstitutionEndorsements.AsNoTracking()
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .Where(e => e.InstitutionId == institutionId && e.Status == TutorEndorsementStatuses.Active && profileIds.Contains(e.TutorProfileId))
            .ToListAsync();

        return teachers.OrderBy(t => t.LastName).ThenBy(t => t.FirstName).Select(t =>
        {
            var p = profiles.FirstOrDefault(x => x.UserId == t.Id);
            var e = p == null ? null : active.FirstOrDefault(x => x.TutorProfileId == p.Id);
            return new InstitutionTutorForEndorsementDto
            {
                TutorUserId = t.Id,
                TutorProfileId = p?.Id,
                TutorName = DisplayName(t),
                AvatarUrl = t.AvatarUrl,
                HasTutorProfile = p != null,
                IsInPersonVerified = p != null && TutorInPersonPolicy.IsVerified(p),
                // Le rattachement est accepté (filtre ci-dessus) : une caution active est valide.
                ActiveEndorsement = e == null ? null : EndorsementDto(e, true),
            };
        }).ToList();
    }

    public async Task<TutorEndorsementDto> EndorseAsync(int institutionUserId, CreateTutorEndorsementDto request)
    {
        var institutionId = await InstitutionOfAsync(institutionUserId);
        var statement = (request.Statement ?? "").Trim();
        var verifier = (request.VerifierName ?? "").Trim();
        if (statement.Length < 20)
            throw new InvalidOperationException("Décris comment votre établissement a vérifié ce répétiteur (20 caractères au moins).");
        if (verifier.Length < 2)
            throw new InvalidOperationException("Indique la personne de l'établissement qui a vérifié ce répétiteur.");
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        if (request.VerifiedOn == default || request.VerifiedOn > today)
            throw new InvalidOperationException("Indique la date de la vérification (aujourd'hui ou avant).");

        var linked = await _db.InstitutionTeacherLinks.AnyAsync(l =>
            l.InstitutionId == institutionId && l.TeacherId == request.TutorUserId && l.Status == "accepted");
        if (!linked)
            throw new InvalidOperationException("Ce répétiteur n'est pas rattaché à votre établissement.");
        var profile = await _db.TutorProfiles.FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce professeur n'a pas encore de profil répétiteur.");
        if (await _db.TutorInstitutionEndorsements.AnyAsync(e => e.TutorProfileId == profile.Id && e.InstitutionId == institutionId && e.Status == TutorEndorsementStatuses.Active))
            throw new InvalidOperationException("Votre établissement cautionne déjà ce répétiteur.");

        var endorsement = new TutorInstitutionEndorsement
        {
            TutorProfileId = profile.Id,
            InstitutionId = institutionId,
            EndorsedByUserId = institutionUserId,
            Statement = Truncate(statement, 500),
            VerifierName = Truncate(verifier, 150),
            VerifiedOn = request.VerifiedOn,
        };
        _db.TutorInstitutionEndorsements.Add(endorsement);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            throw new InvalidOperationException("Votre établissement cautionne déjà ce répétiteur.");
        }
        var institutionName = await _db.Institutions.AsNoTracking().IgnoreQueryFilters().Where(i => i.Id == institutionId).Select(i => i.Name).FirstOrDefaultAsync();
        AddEvent(profile.Id, "institution", "endorsed", institutionUserId, "institution",
            $"{institutionName} : vérifié par {endorsement.VerifierName} le {endorsement.VerifiedOn:dd/MM/yyyy}.", endorsement.Id);
        await _db.SaveChangesAsync();
        await RecomputeAsync(profile.Id, institutionUserId, "institution", $"Caution de {institutionName}.");
        await NotifyTutorAsync(profile.UserId, "Caution de ton établissement",
            $"{institutionName} a confirmé t'avoir vérifié : le badge « Caution : {institutionName} » apparaît sur ton profil.", "TutorInstitutionEndorsement", endorsement.Id);

        var saved = await _db.TutorInstitutionEndorsements.AsNoTracking()
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .FirstAsync(e => e.Id == endorsement.Id);
        return EndorsementDto(saved, true);
    }

    public async Task<TutorEndorsementDto> RevokeEndorsementAsync(int actorUserId, int endorsementId, string? reason, bool asAdmin = false)
    {
        var endorsement = await _db.TutorInstitutionEndorsements
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(e => e.Id == endorsementId)
            ?? throw new InvalidOperationException("Caution introuvable.");
        if (!asAdmin)
        {
            var institutionId = await InstitutionOfAsync(actorUserId);
            if (endorsement.InstitutionId != institutionId)
                throw new UnauthorizedAccessException("Cette caution n'est pas celle de votre établissement.");
        }
        if (endorsement.Status != TutorEndorsementStatuses.Active)
            throw new InvalidOperationException("Cette caution est déjà révoquée.");

        await RevokeCoreAsync(endorsement, actorUserId, asAdmin ? "admin" : "institution",
            string.IsNullOrWhiteSpace(reason) ? "Caution retirée par l'établissement." : reason.Trim());
        return EndorsementDto(endorsement, false);
    }

    private async Task RevokeCoreAsync(TutorInstitutionEndorsement endorsement, int actorUserId, string actorRole, string reason)
    {
        endorsement.Status = TutorEndorsementStatuses.Revoked;
        endorsement.RevokedAt = DateTime.UtcNow;
        endorsement.RevokedByUserId = actorUserId;
        endorsement.RevocationReason = Truncate(reason, 300);
        AddEvent(endorsement.TutorProfileId, "institution", "revoked", actorUserId, actorRole,
            $"{endorsement.Institution?.Name} : {endorsement.RevocationReason}", endorsement.Id);
        await _db.SaveChangesAsync();
        // Retire immédiatement la voie « institution » ; les autres voies restent.
        await RecomputeAsync(endorsement.TutorProfileId, actorUserId, actorRole,
            $"Caution de {endorsement.Institution?.Name} retirée.");
        var tutorUserId = endorsement.TutorProfile?.UserId
            ?? await _db.TutorProfiles.Where(p => p.Id == endorsement.TutorProfileId).Select(p => p.UserId).FirstAsync();
        await NotifyTutorAsync(tutorUserId, "Caution retirée",
            $"{endorsement.Institution?.Name} ne cautionne plus ton profil ({endorsement.RevocationReason}).", "TutorInstitutionEndorsement", endorsement.Id);
    }

    public async Task OnAffiliationEndedAsync(int institutionId, int teacherUserId, int actorUserId, string actorRole)
    {
        var active = await _db.TutorInstitutionEndorsements
            .Include(e => e.Institution).Include(e => e.TutorProfile).ThenInclude(p => p!.User)
            .Where(e => e.InstitutionId == institutionId && e.TutorProfile!.UserId == teacherUserId && e.Status == TutorEndorsementStatuses.Active)
            .ToListAsync();
        foreach (var e in active)
            await RevokeCoreAsync(e, actorUserId, actorRole, "Rattachement à l'établissement révoqué.");
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Admin : pièce d'identité + selfie
    // ═════════════════════════════════════════════════════════════════════

    private static AdminTutorSummaryDto TutorSummary(TutorProfile p) => new()
    {
        TutorProfileId = p.Id,
        TutorUserId = p.UserId,
        Name = DisplayName(p.User),
        Email = p.User?.Email,
        AvatarUrl = p.User?.AvatarUrl,
        IsInPersonVerified = TutorInPersonPolicy.IsVerified(p),
        IsSuspended = TutorInPersonPolicy.IsSuspended(p),
        Routes = string.IsNullOrEmpty(p.InPersonVerificationRoutes) ? new List<string>() : p.InPersonVerificationRoutes.Split(',').ToList(),
    };

    private async Task<Dictionary<int, string?>> NamesAsync(IEnumerable<int?> ids)
    {
        var list = ids.Where(i => i != null).Select(i => i!.Value).Distinct().ToList();
        if (list.Count == 0) return new Dictionary<int, string?>();
        var users = await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => list.Contains(u.Id)).ToListAsync();
        return users.ToDictionary(u => u.Id, u => DisplayName(u));
    }

    private AdminIdentityCheckDto IdentityDto(TutorIdentityCheck c, IReadOnlyDictionary<int, string?> names) => new()
    {
        Id = c.Id,
        Tutor = TutorSummary(c.TutorProfile!),
        Source = c.Source,
        InterviewId = c.TutorVerificationInterviewId,
        DocumentType = c.DocumentType,
        HasBack = c.BackFileRef != null,
        Status = c.Status,
        RejectionReason = c.RejectionReason,
        SubmittedAt = c.SubmittedAt,
        SubmittedByName = names.GetValueOrDefault(c.SubmittedByUserId),
        ReviewedAt = c.ReviewedAt,
        ReviewedByName = c.ReviewedByUserId is int r ? names.GetValueOrDefault(r) : null,
        FilesPurgedAt = c.FilesPurgedAt,
    };

    public async Task<List<AdminIdentityCheckDto>> GetIdentityChecksAsync(string? status)
    {
        var query = _db.TutorIdentityChecks.AsNoTracking().Include(c => c.TutorProfile).ThenInclude(p => p!.User).AsQueryable();
        status = string.IsNullOrWhiteSpace(status) ? TutorIdentityStatuses.Pending : status;
        if (status != "all") query = query.Where(c => c.Status == status);
        var rows = await query.OrderBy(c => c.SubmittedAt).Take(200).ToListAsync();
        var names = await NamesAsync(rows.SelectMany(c => new int?[] { c.SubmittedByUserId, c.ReviewedByUserId }));
        return rows.Select(c => IdentityDto(c, names)).ToList();
    }

    public async Task<AdminIdentityFilesDto> GetIdentityFilesAsync(int checkId, string apiBaseForLocal)
    {
        var check = await _db.TutorIdentityChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkId)
            ?? throw new InvalidOperationException("Dépôt introuvable.");
        if (check.FilesPurgedAt != null || check.FrontFileRef == null)
            throw new InvalidOperationException("Les fichiers de ce dépôt ont été supprimés (règle de conservation).");

        var ttl = _settings.IdentityUrlTtl;
        string? Url(string? reference, string kind) => reference == null ? null
            : _files.GetTemporaryUrl(reference, ttl) ?? $"{apiBaseForLocal.TrimEnd('/')}/{checkId}/files/{kind}";
        var front = Url(check.FrontFileRef, "front");
        return new AdminIdentityFilesDto
        {
            FrontUrl = front,
            BackUrl = Url(check.BackFileRef, "back"),
            SelfieUrl = Url(check.SelfieFileRef, "selfie"),
            RequiresAuth = check.FrontFileRef.StartsWith(PrivateFileStore.LocalPrefix, StringComparison.Ordinal),
            ExpiresAt = DateTime.UtcNow.Add(ttl),
        };
    }

    public async Task<(Stream Content, string ContentType)?> OpenIdentityFileAsync(int checkId, string kind)
    {
        var check = await _db.TutorIdentityChecks.AsNoTracking().FirstOrDefaultAsync(c => c.Id == checkId);
        var reference = kind switch
        {
            "front" => check?.FrontFileRef,
            "back" => check?.BackFileRef,
            "selfie" => check?.SelfieFileRef,
            _ => null,
        };
        return reference == null ? null : await _files.OpenLocalAsync(reference);
    }

    public async Task<AdminIdentityCheckDto> ReviewIdentityAsync(int adminUserId, int checkId, bool approve, string? reason)
    {
        var check = await _db.TutorIdentityChecks.Include(c => c.TutorProfile).ThenInclude(p => p!.User)
            .FirstOrDefaultAsync(c => c.Id == checkId)
            ?? throw new InvalidOperationException("Dépôt introuvable.");
        if (check.Status != TutorIdentityStatuses.Pending)
            throw new InvalidOperationException("Ce dépôt a déjà été traité.");
        if (!approve && string.IsNullOrWhiteSpace(reason))
            throw new InvalidOperationException("Indique le motif du refus : il est transmis au répétiteur.");

        check.Status = approve ? TutorIdentityStatuses.Approved : TutorIdentityStatuses.Rejected;
        check.RejectionReason = approve ? null : Truncate(reason!.Trim(), 500);
        check.ReviewedAt = DateTime.UtcNow;
        check.ReviewedByUserId = adminUserId;
        AddEvent(check.TutorProfileId, "identity", approve ? "approved" : "rejected", adminUserId, "admin", check.RejectionReason, check.Id);
        await _db.SaveChangesAsync();

        var tutorUserId = check.TutorProfile!.UserId;
        if (approve)
        {
            await RecomputeAsync(check.TutorProfileId, adminUserId, "admin", "Pièce d'identité et selfie validés.");
        }
        else
        {
            await NotifyTutorAsync(tutorUserId, "Pièce d'identité non validée",
                $"Motif : {check.RejectionReason}. Tu peux déposer de nouveaux documents depuis ta page de vérification.", "TutorIdentityCheck", check.Id);
        }

        var reloaded = await _db.TutorIdentityChecks.AsNoTracking().Include(c => c.TutorProfile).ThenInclude(p => p!.User).FirstAsync(c => c.Id == checkId);
        return IdentityDto(reloaded, await NamesAsync(new int?[] { reloaded.SubmittedByUserId, reloaded.ReviewedByUserId }));
    }

    public async Task<AdminIdentityCheckDto> SubmitIdentityForInterviewAsync(int adminUserId, int interviewId, string documentType,
        TutorIdentityUpload front, TutorIdentityUpload? back, TutorIdentityUpload selfie)
    {
        var interview = await _db.TutorVerificationInterviews.Include(i => i.TutorProfile)
            .FirstOrDefaultAsync(i => i.Id == interviewId)
            ?? throw new InvalidOperationException("Entretien introuvable.");
        if (interview.Status is TutorInterviewStatuses.Cancelled or TutorInterviewStatuses.NoShow)
            throw new InvalidOperationException("Cet entretien n'a pas eu lieu.");
        var check = await CreateIdentityCheckAsync(interview.TutorProfile!, adminUserId, "admin", TutorIdentitySources.Interview, interview.Id,
            documentType, front, back, selfie);
        var reloaded = await _db.TutorIdentityChecks.AsNoTracking().Include(c => c.TutorProfile).ThenInclude(p => p!.User).FirstAsync(c => c.Id == check.Id);
        return IdentityDto(reloaded, await NamesAsync(new int?[] { reloaded.SubmittedByUserId }));
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Admin : entretien
    // ═════════════════════════════════════════════════════════════════════

    private IQueryable<TutorVerificationInterview> InterviewsWithTutor() => _db.TutorVerificationInterviews
        .Include(i => i.TutorProfile).ThenInclude(p => p!.User)
        .Include(i => i.TutorProfile).ThenInclude(p => p!.Subjects)
        .Include(i => i.TutorProfile).ThenInclude(p => p!.Levels)
        .Include(i => i.TutorProfile).ThenInclude(p => p!.Specialties)
        .AsSplitQuery();

    private async Task<AdminInterviewDto> InterviewDtoAsync(TutorVerificationInterview i)
    {
        var p = i.TutorProfile!;
        var identity = await _db.TutorIdentityChecks.AsNoTracking().Include(c => c.TutorProfile).ThenInclude(x => x!.User)
            .Where(c => c.TutorVerificationInterviewId == i.Id && c.Status != TutorIdentityStatuses.Superseded)
            .OrderByDescending(c => c.SubmittedAt).FirstOrDefaultAsync();
        var names = await NamesAsync(new int?[] { i.AgentUserId, identity?.SubmittedByUserId, identity?.ReviewedByUserId });
        return new AdminInterviewDto
        {
            Id = i.Id,
            Tutor = TutorSummary(p),
            Subjects = p.Subjects.Select(s => s.Subject).ToList(),
            Levels = p.Levels.Select(l => l.Level).ToList(),
            Specialties = p.Specialties.Select(s => s.Label).ToList(),
            TeachingStyle = p.TeachingStyle,
            Bio = p.TutorBio,
            Format = i.Format,
            Status = i.Status,
            Availability = i.Availability,
            City = i.City,
            TutorMessage = i.TutorMessage,
            ScheduledAt = i.ScheduledAt,
            MeetingUrl = i.MeetingUrl,
            Location = i.Location,
            AgentUserId = i.AgentUserId,
            AgentName = i.AgentUserId is int a ? names.GetValueOrDefault(a) : null,
            GuideJson = i.GuideJson,
            AgentNotes = i.AgentNotes,
            ScoresJson = i.ScoresJson,
            ReportDraft = i.ReportDraft,
            FinalReport = i.FinalReport,
            OutcomeReason = i.OutcomeReason,
            RequestedAt = i.RequestedAt,
            CompletedAt = i.CompletedAt,
            Identity = identity == null ? null : IdentityDto(identity, names),
            HasApprovedIdentity = await _db.TutorIdentityChecks.AnyAsync(c => c.TutorProfileId == p.Id && c.Status == TutorIdentityStatuses.Approved),
        };
    }

    public async Task<List<AdminInterviewDto>> GetInterviewsAsync(string? status)
    {
        var query = InterviewsWithTutor().AsNoTracking();
        if (string.IsNullOrWhiteSpace(status) || status == "open")
            query = query.Where(i => TutorInterviewStatuses.Open.Contains(i.Status));
        else if (status != "all")
            query = query.Where(i => i.Status == status);
        var rows = await query.OrderBy(i => i.ScheduledAt ?? i.RequestedAt).Take(200).ToListAsync();
        var result = new List<AdminInterviewDto>();
        foreach (var i in rows) result.Add(await InterviewDtoAsync(i));
        return result;
    }

    public async Task<AdminInterviewDto> GetInterviewAsync(int interviewId)
    {
        var i = await InterviewsWithTutor().AsNoTracking().FirstOrDefaultAsync(x => x.Id == interviewId)
            ?? throw new InvalidOperationException("Entretien introuvable.");
        return await InterviewDtoAsync(i);
    }

    private async Task<TutorVerificationInterview> OpenInterviewAsync(int interviewId)
    {
        var i = await _db.TutorVerificationInterviews.Include(x => x.TutorProfile).FirstOrDefaultAsync(x => x.Id == interviewId)
            ?? throw new InvalidOperationException("Entretien introuvable.");
        if (!TutorInterviewStatuses.Open.Contains(i.Status))
            throw new InvalidOperationException("Cet entretien est terminé.");
        return i;
    }

    public async Task<AdminInterviewDto> ScheduleInterviewAsync(int adminUserId, int interviewId, ScheduleInterviewDto request)
    {
        var i = await OpenInterviewAsync(interviewId);
        if (request.ScheduledAt == default)
            throw new InvalidOperationException("Indique la date et l'heure de l'entretien.");
        var scheduledUtc = request.ScheduledAt.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(request.ScheduledAt, DateTimeKind.Utc)
            : request.ScheduledAt.ToUniversalTime();
        if (i.Format == TutorInterviewFormats.Video)
        {
            var url = (request.MeetingUrl ?? "").Trim();
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
                throw new InvalidOperationException("Indique le lien de la visio (https://…).");
            i.MeetingUrl = Truncate(url, 500);
            i.Location = null;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(request.Location))
                throw new InvalidOperationException("Indique le lieu de la rencontre.");
            i.Location = Truncate(request.Location.Trim(), 300);
            i.MeetingUrl = null;
        }
        var rescheduled = i.Status == TutorInterviewStatuses.Scheduled;
        i.ScheduledAt = scheduledUtc;
        i.Status = TutorInterviewStatuses.Scheduled;
        i.AgentUserId = adminUserId;
        i.UpdatedAt = DateTime.UtcNow;
        AddEvent(i.TutorProfileId, "interview", "scheduled", adminUserId, "admin",
            $"{(rescheduled ? "Replanifié" : "Planifié")} le {scheduledUtc:dd/MM/yyyy HH:mm} UTC", i.Id);
        await _db.SaveChangesAsync();

        var where = i.Format == TutorInterviewFormats.Video ? $"en visio : {i.MeetingUrl}" : $"lieu : {i.Location}";
        await NotifyTutorAsync(i.TutorProfile!.UserId, "Entretien de vérification planifié",
            $"Ton entretien est prévu le {scheduledUtc:dd/MM/yyyy à HH:mm} (UTC), {where}. Prépare ta pièce d'identité : elle sera jointe au dossier avec un selfie.",
            "TutorVerificationInterview", i.Id);
        return await GetInterviewAsync(i.Id);
    }

    public async Task<AdminInterviewDto> SaveInterviewNotesAsync(int adminUserId, int interviewId, SaveInterviewNotesDto request)
    {
        var i = await OpenInterviewAsync(interviewId);
        if (request.Notes != null) i.AgentNotes = request.Notes.Length > 20000 ? request.Notes[..20000] : request.Notes;
        if (request.ScoresJson != null) i.ScoresJson = request.ScoresJson.Length > 8000 ? request.ScoresJson[..8000] : request.ScoresJson;
        i.AgentUserId ??= adminUserId;
        i.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetInterviewAsync(i.Id);
    }

    public async Task<AdminInterviewDto> SaveInterviewAiAsync(int interviewId, string? guideJson, string? reportDraft)
    {
        var i = await _db.TutorVerificationInterviews.FirstOrDefaultAsync(x => x.Id == interviewId)
            ?? throw new InvalidOperationException("Entretien introuvable.");
        if (guideJson != null) i.GuideJson = guideJson;
        if (reportDraft != null) i.ReportDraft = reportDraft;
        i.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetInterviewAsync(i.Id);
    }

    public async Task<AdminInterviewDto> CompleteInterviewAsync(int adminUserId, int interviewId, CompleteInterviewDto request)
    {
        var i = await OpenInterviewAsync(interviewId);
        var outcome = (request.Outcome ?? "").Trim().ToLowerInvariant();
        if (outcome is not (TutorInterviewStatuses.Passed or TutorInterviewStatuses.Failed or TutorInterviewStatuses.NoShow or TutorInterviewStatuses.Cancelled))
            throw new InvalidOperationException("Résultat invalide (réussi, échoué, absent ou annulé).");
        if (outcome is TutorInterviewStatuses.Passed or TutorInterviewStatuses.Failed)
        {
            if (i.Status != TutorInterviewStatuses.Scheduled)
                throw new InvalidOperationException("Planifie et mène l'entretien avant d'enregistrer son résultat.");
            if (string.IsNullOrWhiteSpace(request.FinalReport) || request.FinalReport.Trim().Length < 30)
                throw new InvalidOperationException("Rédige le compte rendu de l'entretien (30 caractères au moins) avant d'enregistrer le résultat.");
        }
        if (outcome != TutorInterviewStatuses.Passed && string.IsNullOrWhiteSpace(request.Reason))
            throw new InvalidOperationException("Indique le motif : il est transmis au répétiteur.");

        i.Status = outcome;
        i.FinalReport = string.IsNullOrWhiteSpace(request.FinalReport) ? i.FinalReport : request.FinalReport.Trim();
        i.OutcomeReason = string.IsNullOrWhiteSpace(request.Reason) ? null : Truncate(request.Reason.Trim(), 500);
        i.AgentUserId ??= adminUserId;
        i.CompletedAt = DateTime.UtcNow;
        i.UpdatedAt = DateTime.UtcNow;
        AddEvent(i.TutorProfileId, "interview", outcome, adminUserId, "admin", i.OutcomeReason, i.Id);
        await _db.SaveChangesAsync();

        var verified = await RecomputeAsync(i.TutorProfileId, adminUserId, "admin",
            outcome == TutorInterviewStatuses.Passed ? "Entretien réussi." : null);
        var tutorUserId = i.TutorProfile!.UserId;
        if (outcome == TutorInterviewStatuses.Passed)
        {
            if (!verified)
                await NotifyTutorAsync(tutorUserId, "Entretien réussi",
                    "Ton entretien est validé. Il reste à valider ta pièce d'identité et ton selfie : sans eux, le présentiel n'est pas ouvert.",
                    "TutorVerificationInterview", i.Id);
        }
        else
        {
            var title = outcome switch
            {
                TutorInterviewStatuses.Failed => "Entretien non concluant",
                TutorInterviewStatuses.NoShow => "Entretien manqué",
                _ => "Entretien annulé",
            };
            await NotifyTutorAsync(tutorUserId, title, $"Motif : {i.OutcomeReason}. Tu peux demander un nouvel entretien ou choisir une autre voie de vérification.",
                "TutorVerificationInterview", i.Id);
        }
        return await GetInterviewAsync(i.Id);
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Contrôle et historique
    // ═════════════════════════════════════════════════════════════════════

    /// <summary>Séances présentielles à venir encore actives (pour le contrôle et les alertes).</summary>
    public static IQueryable<TutorBooking> UpcomingInPersonQuery(ApplicationDbContext db)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        return db.TutorBookings.AsNoTracking()
            .Where(b => TutorInPersonPolicy.InPersonModes.Contains(b.Mode)
                        && UpcomingStatuses.Contains(b.Status)
                        && b.SessionDate >= today);
    }

    public static AdminInPersonBookingDto InPersonBookingDto(TutorBooking b) => new()
    {
        BookingId = b.Id,
        TutorProfileId = b.TutorProfileId,
        TutorName = DisplayName(b.TutorProfile?.User),
        SessionDate = b.SessionDate,
        StartTime = b.StartTime.ToString(@"hh\:mm"),
        Mode = b.Mode,
        Status = b.Status,
        PayerName = DisplayName(b.Payer) ?? DisplayName(b.Student),
    };

    public async Task<AdminInPersonControlDto> GetInPersonControlAsync()
    {
        var query = UpcomingInPersonQuery(_db)
            .Where(b => b.TutorProfile!.InPersonVerifiedAt == null || b.TutorProfile.InPersonSuspendedAt != null);
        var rows = await query.Include(b => b.TutorProfile).ThenInclude(p => p!.User).Include(b => b.Payer).Include(b => b.Student)
            .OrderBy(b => b.SessionDate).ThenBy(b => b.StartTime).Take(500).ToListAsync();
        return new AdminInPersonControlDto { Count = await query.CountAsync(), Bookings = rows.Select(InPersonBookingDto).ToList() };
    }

    public async Task<List<TutorVerificationEventDto>> GetHistoryAsync(int tutorProfileId, bool forTutor)
    {
        var query = _db.TutorVerificationEvents.AsNoTracking().Where(e => e.TutorProfileId == tutorProfileId);
        // Le tuteur ne voit pas les signalements (protection de ceux qui signalent),
        // seulement la suspension et sa levée.
        if (forTutor) query = query.Where(e => e.Category != "report");
        var rows = await query.OrderByDescending(e => e.CreatedAt).ThenByDescending(e => e.Id).Take(200).ToListAsync();
        var names = forTutor ? new Dictionary<int, string?>() : await NamesAsync(rows.Select(r => r.ActorUserId));
        return rows.Select(e => new TutorVerificationEventDto
        {
            Id = e.Id,
            Category = e.Category,
            Action = e.Action,
            ActorRole = e.ActorRole,
            ActorUserId = forTutor ? null : e.ActorUserId,
            ActorName = e.ActorUserId is int a ? names.GetValueOrDefault(a) : null,
            Reason = e.Reason,
            RelatedId = e.RelatedId,
            CreatedAt = e.CreatedAt,
        }).ToList();
    }

    // ═════════════════════════════════════════════════════════════════════
    //  Conservation des fichiers d'identité (RGPD)
    // ═════════════════════════════════════════════════════════════════════

    private async Task PurgeFilesAsync(TutorIdentityCheck check)
    {
        foreach (var reference in new[] { check.FrontFileRef, check.BackFileRef, check.SelfieFileRef })
            if (reference != null) await _files.DeleteAsync(reference);
        check.FrontFileRef = null;
        check.BackFileRef = null;
        check.SelfieFileRef = null;
        check.FilesPurgedAt = DateTime.UtcNow;
    }

    public async Task<int> PurgeExpiredIdentityFilesAsync(DateTime nowUtc)
    {
        var approvedBefore = nowUtc.AddDays(-_settings.IdentityRetentionDaysAfterApproval);
        var rejectedBefore = nowUtc.AddDays(-_settings.IdentityRetentionDaysAfterRejection);
        var due = await _db.TutorIdentityChecks
            .Where(c => c.FilesPurgedAt == null && c.ReviewedAt != null
                        && ((c.Status == TutorIdentityStatuses.Approved && c.ReviewedAt <= approvedBefore)
                            || (c.Status == TutorIdentityStatuses.Rejected && c.ReviewedAt <= rejectedBefore)))
            .OrderBy(c => c.ReviewedAt).Take(200).ToListAsync();
        foreach (var c in due)
        {
            await PurgeFilesAsync(c);
            AddEvent(c.TutorProfileId, "identity", "files_purged", null, "system",
                c.Status == TutorIdentityStatuses.Approved
                    ? $"Fichiers supprimés {_settings.IdentityRetentionDaysAfterApproval} jours après la validation (la validation reste acquise)."
                    : $"Fichiers supprimés {_settings.IdentityRetentionDaysAfterRejection} jours après le refus.", c.Id);
        }
        if (due.Count > 0) await _db.SaveChangesAsync();
        return due.Count;
    }
}
