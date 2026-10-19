using System.Text.Json;
using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Accès refusé au support d'une séance (compte ni tuteur, ni participant, ni payeur).</summary>
public sealed class TutorSessionMaterialForbiddenException : InvalidOperationException
{
    public TutorSessionMaterialForbiddenException(string message) : base(message) { }
}

/// <summary>Échec de la génération WinAI (service indisponible ou réponse inutilisable).</summary>
public sealed class TutorSessionMaterialGenerationException : InvalidOperationException
{
    public TutorSessionMaterialGenerationException(string message) : base(message) { }
}

public interface ITutorSessionMaterialService
{
    Task<TutorSessionMaterialViewDto> GetAsync(int userId, int bookingId);

    /// <summary>
    /// Génère (ou régénère) la copie de travail du support. Refus du plan ou du
    /// budget de tokens renvoyé dans <c>Refusal</c> (403, 402 ou 503).
    /// </summary>
    Task<(IActionResult? Refusal, TutorSessionMaterialViewDto? View)> GenerateAsync(
        int tutorUserId, int bookingId, string? instructions, string? authorization, bool automatic = false, CancellationToken ct = default);

    Task<TutorSessionMaterialViewDto> UpdateAsync(int tutorUserId, int bookingId, TutorSessionMaterialContent content);
    Task<TutorSessionMaterialViewDto> ShareAsync(int tutorUserId, int bookingId);
    Task<TutorSessionMaterialViewDto> SubmitAsync(int studentUserId, int bookingId, SubmitTutorSessionMaterialRequestDto request);

    Task<TutorSessionMaterialSettingsDto> GetSettingsAsync(int tutorUserId);
    Task<TutorSessionMaterialSettingsDto> SetSettingsAsync(int tutorUserId, bool autoGenerateOnAccept);
}

/// <summary>
/// Lot 9.6 (suivi 17.5, partie génération) : support de séance WinAI.
/// <list type="bullet">
///   <item><b>Qui</b> : le tuteur de la séance génère, relance, modifie et
///   partage ; les élèves participants consultent et répondent au quiz et aux
///   exercices ; le côté payeur (parent, institution, membre du groupe qui a
///   réservé) consulte en lecture seule. Personne d'autre.</item>
///   <item><b>Coût</b> : droit <c>ai.feature.session_material</c> et débit des
///   tokens réels sur le budget DU TUTEUR (<see cref="IAiFeatureGate"/>).</item>
///   <item><b>Versions</b> : copie de travail (brouillon) et version partagée,
///   seule visible des participants et du payeur.</item>
///   <item><b>Corrigés</b> : bonnes réponses, explications et corrigés ne sont
///   envoyés à un élève qu'après l'envoi de ses réponses, jamais au payeur.</item>
///   <item>Le support s'affiche dans l'application ; aucun fichier n'est produit
///   (ce n'est ni une épreuve ni un livre du catalogue, décision 18.21).</item>
/// </list>
/// </summary>
public class TutorSessionMaterialService : ITutorSessionMaterialService
{
    public const string FastApiRoute = "/api/teacher/session-material";

    /// <summary>Statuts où le tuteur peut générer ou modifier le support (avant, pendant ou après la séance).</summary>
    public static readonly string[] EditableStatuses = { "pending_tutor_approval", "confirmed", "completed", "disputed" };
    /// <summary>Statuts où le support peut être partagé et consulté par les participants : la séance a été acceptée.</summary>
    public static readonly string[] SharedStatuses = { "confirmed", "completed", "disputed" };

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly ApplicationDbContext _db;
    private readonly IAiFeatureGate _gate;
    private readonly IHttpClientFactory _http;
    private readonly INtfyService _ntfy;
    private readonly ILogger<TutorSessionMaterialService> _logger;

    public TutorSessionMaterialService(ApplicationDbContext db, IAiFeatureGate gate, IHttpClientFactory http, INtfyService ntfy,
        ILogger<TutorSessionMaterialService> logger)
    {
        _db = db;
        _gate = gate;
        _http = http;
        _ntfy = ntfy;
        _logger = logger;
    }

    // ── Accès ────────────────────────────────────────────────────────────

    private enum Viewer { None, Tutor, Participant, Payer }

    private async Task<TutorBooking> LoadBookingAsync(int bookingId) =>
        await _db.TutorBookings
            .Include(b => b.TutorProfile).ThenInclude(p => p!.User)
            .Include(b => b.Topics)
            .Include(b => b.Student)
            .Include(b => b.Participants).ThenInclude(p => p.Student)
            .FirstOrDefaultAsync(b => b.Id == bookingId)
        ?? throw new InvalidOperationException("Séance introuvable.");

    private static bool IsParticipant(TutorBooking b, int userId) =>
        b.StudentUserId == userId || b.Participants.Any(p => p.StudentUserId == userId);

    private static IEnumerable<int> ParticipantIds(TutorBooking b) =>
        b.Participants.Count > 0 ? b.Participants.Select(p => p.StudentUserId).Distinct() : new[] { b.StudentUserId };

    private async Task<Viewer> ViewerOfAsync(TutorBooking b, int userId)
    {
        if (b.TutorProfile?.UserId == userId) return Viewer.Tutor;
        if (IsParticipant(b, userId)) return Viewer.Participant;
        // Côté payeur (même règle que TutorBookingService.IsPayerSide, lot 9.3) :
        // portefeuille payeur, compte qui a réservé, tout compte de l'institution payeuse.
        if ((b.PayerUserId ?? b.StudentUserId) == userId || b.BookedByUserId == userId) return Viewer.Payer;
        if (b.PayerInstitutionId is int iid)
        {
            var viewerInstitution = await _db.Users.AsNoTracking()
                .Where(u => u.Id == userId && u.Role == "institution")
                .Select(u => u.InstitutionId).FirstOrDefaultAsync();
            if (viewerInstitution == iid) return Viewer.Payer;
        }
        return Viewer.None;
    }

    private async Task<TutorBooking> LoadForTutorAsync(int tutorUserId, int bookingId)
    {
        var b = await LoadBookingAsync(bookingId);
        if (b.TutorProfile?.UserId != tutorUserId)
            throw new TutorSessionMaterialForbiddenException("Seul le tuteur de la séance prépare son support.");
        return b;
    }

    private static string? EditBlockedReason(TutorBooking b) =>
        EditableStatuses.Contains(b.Status) ? null : b.Status switch
        {
            "pending_payment" => "La séance n'est pas encore payée.",
            "cancelled" => "La séance est annulée.",
            "rejected" => "La séance a été refusée.",
            "expired" => "La demande a expiré.",
            _ => "Le support n'est pas disponible pour cette séance.",
        };

    // ── Lecture ──────────────────────────────────────────────────────────

    public async Task<TutorSessionMaterialViewDto> GetAsync(int userId, int bookingId)
    {
        var b = await LoadBookingAsync(bookingId);
        var viewer = await ViewerOfAsync(b, userId);
        if (viewer == Viewer.None)
            throw new TutorSessionMaterialForbiddenException("Tu n'as pas accès au support de cette séance.");

        var material = await _db.TutorSessionMaterials
            .Include(m => m.Results).ThenInclude(r => r.Student)
            .FirstOrDefaultAsync(m => m.TutorBookingId == bookingId);

        // Remontée au tuteur : les résultats consultés sont marqués vus.
        if (viewer == Viewer.Tutor && material != null && material.Results.Any(r => r.SeenByTutorAt == null))
        {
            foreach (var r in material.Results.Where(r => r.SeenByTutorAt == null)) r.SeenByTutorAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();
        }

        return await BuildViewAsync(b, material, viewer, userId);
    }

    private Task<TutorSessionMaterialViewDto> BuildViewAsync(TutorBooking b, TutorSessionMaterial? m, Viewer viewer, int userId)
    {
        var participants = (b.Participants.Count > 0
                ? b.Participants.OrderBy(p => p.StudentUserId == b.StudentUserId ? 0 : 1).ThenBy(p => p.Id)
                    .Select(p => new TutorBookingParticipantDto { UserId = p.StudentUserId, Name = DisplayName(p.Student), AvatarUrl = p.Student?.AvatarUrl })
                : new[] { new TutorBookingParticipantDto { UserId = b.StudentUserId, Name = DisplayName(b.Student), AvatarUrl = b.Student?.AvatarUrl } })
            .ToList();
        var dto = new TutorSessionMaterialViewDto
        {
            BookingId = b.Id,
            ViewerRole = viewer switch { Viewer.Tutor => "tutor", Viewer.Participant => "participant", _ => "payer" },
            BookingStatus = b.Status,
            Subject = b.Subject,
            Topics = b.Topics.Select(t => t.Label).ToList(),
            SessionDate = b.SessionDate,
            StartTime = b.StartTime.ToString(@"hh\:mm"),
            EndTime = b.EndTime.ToString(@"hh\:mm"),
            DurationMinutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            TutorName = DisplayName(b.TutorProfile?.User),
            ParticipantCount = Math.Max(b.ParticipantCount, participants.Count),
            Participants = participants,
            TutorEngagementId = b.TutorEngagementId,
        };

        var editBlocked = EditBlockedReason(b);
        var sharedAllowed = SharedStatuses.Contains(b.Status);

        if (viewer == Viewer.Tutor)
        {
            dto.CanGenerate = editBlocked == null;
            dto.CanEdit = editBlocked == null && m != null;
            dto.CanShare = sharedAllowed && m != null && m.SharedVersion != m.Version;
            dto.BlockedReason = editBlocked ?? (sharedAllowed ? null : "Tu pourras partager le support une fois la séance acceptée.");
            if (m == null) return Task.FromResult(dto);

            FillMeta(dto, m);
            dto.Status = m.Status;
            dto.Version = m.Version;
            dto.HasUnsharedChanges = m.SharedVersion != m.Version;
            dto.GenerationCount = m.GenerationCount;
            dto.AutoGenerated = m.AutoGenerated;
            dto.Content = Parse(m.ContentJson);
            dto.SolutionsIncluded = true;
            var shared = m.SharedContentJson != null ? Parse(m.SharedContentJson) : null;
            dto.Results = m.Results.OrderByDescending(r => r.SubmittedAt)
                .Select(r => ResultDto(r, shared, m.SharedVersion, detailed: true)).ToList();
            return Task.FromResult(dto);
        }

        // Participants et payeur : seulement la version partagée, séance acceptée.
        if (m?.SharedContentJson == null || !sharedAllowed)
        {
            dto.BlockedReason = m?.SharedContentJson != null && !sharedAllowed ? EditBlockedReason(b) : null;
            return Task.FromResult(dto);
        }

        FillMeta(dto, m);
        dto.Status = TutorSessionMaterialStatuses.Shared;
        dto.Version = m.SharedVersion;
        var content = Parse(m.SharedContentJson);

        if (viewer == Viewer.Participant)
        {
            var mine = m.Results.FirstOrDefault(r => r.StudentUserId == userId);
            var answeredCurrent = mine != null && mine.MaterialVersion == m.SharedVersion;
            dto.CanSubmit = !answeredCurrent && (content.Quiz.Count > 0 || content.Exercises.Count > 0);
            dto.SolutionsIncluded = answeredCurrent;
            dto.Content = answeredCurrent ? content : WithoutSolutions(content);
            if (mine != null) dto.MyResult = ResultDto(mine, content, m.SharedVersion, detailed: true);
        }
        else
        {
            // Payeur : lecture seule, sans corrigés (un enfant ne doit pas les
            // y trouver avant d'avoir répondu). Parent et institution voient la
            // note de chaque participant ; un membre de groupe payeur non
            // participant ne voit pas les notes des autres.
            dto.Content = WithoutSolutions(content);
            var payerSeesScores = b.PayerType is TutorPayerTypes.Parent or TutorPayerTypes.Institution;
            if (payerSeesScores)
                dto.Results = m.Results.OrderByDescending(r => r.SubmittedAt)
                    .Select(r => ResultDto(r, content, m.SharedVersion, detailed: false)).ToList();
        }
        return Task.FromResult(dto);
    }

    private static void FillMeta(TutorSessionMaterialViewDto dto, TutorSessionMaterial m)
    {
        dto.Exists = true;
        dto.SharedVersion = m.SharedVersion;
        dto.GeneratedAt = m.GeneratedAt;
        dto.SharedAt = m.SharedAt;
        dto.UpdatedAt = m.UpdatedAt ?? m.CreatedAt;
    }

    private static TutorSessionMaterialResultDto ResultDto(TutorSessionMaterialResult r, TutorSessionMaterialContent? shared, int? sharedVersion, bool detailed)
    {
        var dto = new TutorSessionMaterialResultDto
        {
            StudentUserId = r.StudentUserId,
            StudentName = DisplayName(r.Student),
            MaterialVersion = r.MaterialVersion,
            QuizScore = r.QuizScore,
            QuizTotal = r.QuizTotal,
            SubmittedAt = r.SubmittedAt,
            IsOutdated = sharedVersion != null && r.MaterialVersion != sharedVersion,
        };
        if (!detailed) return dto;
        var answers = ParseMap(r.QuizAnswersJson);
        dto.ExerciseAnswers = ParseMap(r.ExerciseAnswersJson);
        // Correction recalculée sur la version partagée seulement si l'élève y a répondu.
        if (shared != null && !dto.IsOutdated)
            dto.Questions = shared.Quiz.Select(q =>
            {
                answers.TryGetValue(q.Id, out var a);
                return new TutorSessionMaterialQuestionResultDto
                {
                    QuestionId = q.Id, Answer = a, CorrectOptionId = q.CorrectOptionId,
                    IsCorrect = a != null && a == q.CorrectOptionId,
                };
            }).ToList();
        else
            dto.Questions = answers.Select(kv => new TutorSessionMaterialQuestionResultDto { QuestionId = kv.Key, Answer = kv.Value }).ToList();
        return dto;
    }

    // ── Génération (tuteur, débitée sur son budget) ─────────────────────

    public async Task<(IActionResult? Refusal, TutorSessionMaterialViewDto? View)> GenerateAsync(
        int tutorUserId, int bookingId, string? instructions, string? authorization, bool automatic = false, CancellationToken ct = default)
    {
        var b = await LoadForTutorAsync(tutorUserId, bookingId);
        if (EditBlockedReason(b) is string blocked) throw new InvalidOperationException(blocked);
        if (b.Topics.Count == 0 && string.IsNullOrWhiteSpace(b.Subject) && string.IsNullOrWhiteSpace(instructions))
            throw new InvalidOperationException("Aucun sujet n'a été indiqué pour cette séance : précise ce que tu veux travailler dans la consigne.");

        var participantIds = ParticipantIds(b).ToList();
        var levels = await _db.Users.AsNoTracking()
            .Where(u => participantIds.Contains(u.Id))
            .Select(u => u.Level).ToListAsync(ct);
        var payload = new
        {
            booking_id = b.Id,
            subject = b.Subject,
            topics = b.Topics.Select(t => t.Label).ToList(),
            duration_minutes = (int)(b.EndTime - b.StartTime).TotalMinutes,
            mode = b.Mode,
            participant_count = participantIds.Count,
            levels = levels.Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l!.Trim()).ToList(),
            instructions = string.IsNullOrWhiteSpace(instructions) ? null : instructions.Trim()[..Math.Min(instructions.Trim().Length, 500)],
        };

        var client = _http.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, FastApiRoute) { Content = JsonContent(payload) };
        if (!string.IsNullOrEmpty(authorization)) req.Headers.TryAddWithoutValidation("Authorization", authorization);

        // Droit du plan + réserve sur le budget du TUTEUR, puis débit du coût
        // réel (en-tête X-WinAI-Tokens) ; réserve rendue si rien n'est servi.
        var (ticket, res) = await AiGateProxy.SendAsync(_gate, tutorUserId, EntitlementCatalog.AiSessionMaterial, "", client, req, ct);
        if (!ticket.Allowed) return (ticket.Refusal, null);

        TutorSessionMaterialContent content;
        using (res)
        {
            var body = await res!.Content.ReadAsStringAsync(ct);
            if (!res.IsSuccessStatusCode)
            {
                _logger.LogWarning("Support de séance #{Booking} : FastAPI {Status} {Body}", b.Id, (int)res.StatusCode, body.Length > 300 ? body[..300] : body);
                throw new TutorSessionMaterialGenerationException("WinAI n'a pas pu générer le support pour le moment. Réessaie dans un instant.");
            }
            try { content = JsonSerializer.Deserialize<TutorSessionMaterialContent>(body, Json) ?? new(); }
            catch (JsonException) { throw new TutorSessionMaterialGenerationException("La réponse de WinAI est inutilisable. Relance la génération."); }
        }
        content = Sanitize(content, b);
        if (content.Course.Sections.Count == 0 && content.Quiz.Count == 0 && content.Exercises.Count == 0)
            throw new TutorSessionMaterialGenerationException("WinAI n'a rien produit d'exploitable. Relance la génération ou précise la consigne.");

        var now = DateTime.UtcNow;
        var m = await _db.TutorSessionMaterials.FirstOrDefaultAsync(x => x.TutorBookingId == b.Id, ct);
        if (m == null)
        {
            m = new TutorSessionMaterial { TutorBookingId = b.Id, TutorUserId = tutorUserId, Version = 1, CreatedAt = now, AutoGenerated = automatic };
            _db.TutorSessionMaterials.Add(m);
        }
        else
        {
            m.Version++;
            m.UpdatedAt = now;
        }
        m.Title = Clip(content.Title, 200);
        m.ContentJson = JsonSerializer.Serialize(content, Json);
        m.LastChange = "generated";
        m.GenerationCount++;
        m.GeneratedAt = now;
        await _db.SaveChangesAsync(CancellationToken.None);

        return (null, await GetAsync(tutorUserId, b.Id));
    }

    private static HttpContent JsonContent(object payload) => System.Net.Http.Json.JsonContent.Create(payload);

    // ── Modification et partage (tuteur) ────────────────────────────────

    public async Task<TutorSessionMaterialViewDto> UpdateAsync(int tutorUserId, int bookingId, TutorSessionMaterialContent content)
    {
        var b = await LoadForTutorAsync(tutorUserId, bookingId);
        if (EditBlockedReason(b) is string blocked) throw new InvalidOperationException(blocked);
        var m = await _db.TutorSessionMaterials.FirstOrDefaultAsync(x => x.TutorBookingId == b.Id)
            ?? throw new InvalidOperationException("Génère d'abord le support avec WinAI.");
        var clean = Sanitize(content ?? new(), b);
        if (string.IsNullOrWhiteSpace(clean.Title))
            throw new InvalidOperationException("Le support doit avoir un titre.");
        m.Title = Clip(clean.Title, 200);
        m.ContentJson = JsonSerializer.Serialize(clean, Json);
        m.Version++;
        m.LastChange = "edited";
        m.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return await GetAsync(tutorUserId, b.Id);
    }

    public async Task<TutorSessionMaterialViewDto> ShareAsync(int tutorUserId, int bookingId)
    {
        var b = await LoadForTutorAsync(tutorUserId, bookingId);
        if (!SharedStatuses.Contains(b.Status))
            throw new InvalidOperationException(EditBlockedReason(b) ?? "Tu pourras partager le support une fois la séance acceptée.");
        var m = await _db.TutorSessionMaterials.FirstOrDefaultAsync(x => x.TutorBookingId == b.Id)
            ?? throw new InvalidOperationException("Génère d'abord le support avec WinAI.");
        if (m.SharedVersion == m.Version)
            throw new InvalidOperationException("Cette version est déjà partagée.");

        var firstShare = m.SharedVersion == null;
        m.SharedContentJson = m.ContentJson;
        m.SharedVersion = m.Version;
        m.Status = TutorSessionMaterialStatuses.Shared;
        m.SharedAt = DateTime.UtcNow;
        m.UpdatedAt = m.SharedAt;
        await _db.SaveChangesAsync();

        // Participants et payeur prévenus (le payeur en lecture seule).
        var ids = new HashSet<int>(ParticipantIds(b)) { b.PayerUserId ?? b.StudentUserId };
        ids.Remove(tutorUserId);
        var when = b.SessionDate.ToString("dd/MM/yyyy");
        foreach (var id in ids)
        {
            try
            {
                // Lot 10.6 : dans la langue du destinataire (User.Locale).
                await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{id}",
                    firstShare ? new("Support de séance disponible", "Session material available") : new("Support de séance mis à jour", "Session material updated"),
                    new($"Le tuteur a partagé le support de la séance du {when} : cours, quiz et exercices dans l'application.",
                        $"The tutor has shared the material for the session on {when}: lesson, quiz and exercises in the app."),
                    userId: id, type: "TutorBooking");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Notification du support #{Booking} vers {User} impossible", b.Id, id); }
        }
        return await GetAsync(tutorUserId, b.Id);
    }

    // ── Réponses d'un élève participant ─────────────────────────────────

    public async Task<TutorSessionMaterialViewDto> SubmitAsync(int studentUserId, int bookingId, SubmitTutorSessionMaterialRequestDto request)
    {
        var b = await LoadBookingAsync(bookingId);
        if (!IsParticipant(b, studentUserId))
            throw new TutorSessionMaterialForbiddenException("Seuls les élèves de la séance répondent au quiz et aux exercices.");
        if (!SharedStatuses.Contains(b.Status))
            throw new InvalidOperationException(EditBlockedReason(b) ?? "Le support n'est pas disponible.");
        var m = await _db.TutorSessionMaterials.Include(x => x.Results)
            .FirstOrDefaultAsync(x => x.TutorBookingId == b.Id);
        if (m?.SharedContentJson == null || m.SharedVersion is not int version)
            throw new InvalidOperationException("Le tuteur n'a pas encore partagé le support.");

        var existing = m.Results.FirstOrDefault(r => r.StudentUserId == studentUserId);
        if (existing != null && existing.MaterialVersion == version)
            throw new InvalidOperationException("Tu as déjà envoyé tes réponses pour cette version du support.");

        var content = Parse(m.SharedContentJson);
        var quizAnswers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var q in content.Quiz)
            if (request.QuizAnswers != null && request.QuizAnswers.TryGetValue(q.Id, out var a) && q.Options.Any(o => o.Id == a))
                quizAnswers[q.Id] = a;
        var exerciseAnswers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var e in content.Exercises)
            if (request.ExerciseAnswers != null && request.ExerciseAnswers.TryGetValue(e.Id, out var t) && !string.IsNullOrWhiteSpace(t))
                exerciseAnswers[e.Id] = Clip(t.Trim(), 4000);
        if (quizAnswers.Count == 0 && exerciseAnswers.Count == 0)
            throw new InvalidOperationException("Réponds au moins à une question ou à un exercice.");

        var score = content.Quiz.Count(q => quizAnswers.TryGetValue(q.Id, out var a) && a == q.CorrectOptionId);
        var row = existing ?? new TutorSessionMaterialResult { TutorSessionMaterialId = m.Id, StudentUserId = studentUserId };
        row.MaterialVersion = version;
        row.QuizAnswersJson = JsonSerializer.Serialize(quizAnswers, Json);
        row.ExerciseAnswersJson = JsonSerializer.Serialize(exerciseAnswers, Json);
        row.QuizScore = score;
        row.QuizTotal = content.Quiz.Count;
        row.SubmittedAt = DateTime.UtcNow;
        row.SeenByTutorAt = null;
        if (existing == null) _db.TutorSessionMaterialResults.Add(row);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Double envoi simultané : l'index unique (support, élève) garde le premier.
            throw new InvalidOperationException("Tes réponses ont déjà été enregistrées.");
        }

        // Remontée au tuteur.
        if (b.TutorProfile?.UserId is int tutorId)
        {
            var student = DisplayName(await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == studentUserId));
            var name = student ?? "Un élève";
            var detail = content.Quiz.Count > 0 ? $"quiz {score}/{content.Quiz.Count}" : "exercices rendus";
            var detailEn = content.Quiz.Count > 0 ? $"quiz {score}/{content.Quiz.Count}" : "exercises handed in";
            try
            {
                await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{tutorId}", new("Support de séance : réponses reçues", "Session material: answers received"),
                    new($"{name} a répondu au support de la séance du {b.SessionDate:dd/MM/yyyy} ({detail}).",
                        $"{student ?? "A student"} has answered the material for the session on {b.SessionDate:dd/MM/yyyy} ({detailEn})."),
                    userId: tutorId, type: "TutorBooking");
            }
            catch (Exception ex) { _logger.LogWarning(ex, "Notification du tuteur (support #{Booking}) impossible", b.Id); }
        }
        return await GetAsync(studentUserId, b.Id);
    }

    // ── Réglage : génération automatique à l'acceptation ────────────────

    public async Task<TutorSessionMaterialSettingsDto> GetSettingsAsync(int tutorUserId)
    {
        var profile = await _db.TutorProfiles.AsNoTracking().FirstOrDefaultAsync(p => p.UserId == tutorUserId)
            ?? throw new InvalidOperationException("Active d'abord ton profil de tuteur.");
        return new TutorSessionMaterialSettingsDto { AutoGenerateOnAccept = profile.AutoGenerateSessionMaterial };
    }

    public async Task<TutorSessionMaterialSettingsDto> SetSettingsAsync(int tutorUserId, bool autoGenerateOnAccept)
    {
        var profile = await _db.TutorProfiles.FirstOrDefaultAsync(p => p.UserId == tutorUserId)
            ?? throw new InvalidOperationException("Active d'abord ton profil de tuteur.");
        profile.AutoGenerateSessionMaterial = autoGenerateOnAccept;
        profile.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return new TutorSessionMaterialSettingsDto { AutoGenerateOnAccept = autoGenerateOnAccept };
    }

    // ── Contenu : lecture, nettoyage, masquage des corrigés ─────────────

    public static TutorSessionMaterialContent Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<TutorSessionMaterialContent>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    private static Dictionary<string, string> ParseMap(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new();
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(json, Json) ?? new(); }
        catch (JsonException) { return new(); }
    }

    /// <summary>Copie sans bonnes réponses, explications ni corrigés.</summary>
    public static TutorSessionMaterialContent WithoutSolutions(TutorSessionMaterialContent c) => new()
    {
        Title = c.Title, Subject = c.Subject, Level = c.Level, Objectives = c.Objectives, Course = c.Course, GroupNote = c.GroupNote,
        Quiz = c.Quiz.Select(q => new TutorSessionMaterialQuizQuestion { Id = q.Id, Text = q.Text, Options = q.Options }).ToList(),
        Exercises = c.Exercises.Select(e => new TutorSessionMaterialExercise
        {
            Id = e.Id, Title = e.Title, Statement = e.Statement, Difficulty = e.Difficulty, Hints = e.Hints,
        }).ToList(),
    };

    public const int MaxSections = 8, MaxQuiz = 15, MaxExercises = 8, MaxObjectives = 6, MaxKeyPoints = 10, MaxHints = 4;

    /// <summary>
    /// Bornes et cohérence, pour une sortie WinAI comme pour une modification
    /// du tuteur : longueurs, nombres d'éléments, identifiants uniques
    /// (q1…, e1…, options a…f), bonne réponse parmi les options.
    /// </summary>
    public static TutorSessionMaterialContent Sanitize(TutorSessionMaterialContent c, TutorBooking? b = null)
    {
        static List<string> Lines(IEnumerable<string>? items, int max, int len) =>
            (items ?? Enumerable.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).Select(s => Clip(s.Trim(), len)).Take(max).ToList();

        var result = new TutorSessionMaterialContent
        {
            Title = Clip((c.Title ?? "").Trim(), 200),
            Subject = NullIfEmpty(Clip((c.Subject ?? b?.Subject ?? "").Trim(), 100)),
            Level = NullIfEmpty(Clip((c.Level ?? "").Trim(), 100)),
            Objectives = Lines(c.Objectives, MaxObjectives, 300),
            GroupNote = NullIfEmpty(Clip((c.GroupNote ?? "").Trim(), 1000)),
            Course = new TutorSessionMaterialCourse
            {
                Introduction = Clip((c.Course?.Introduction ?? "").Trim(), 3000),
                Summary = Clip((c.Course?.Summary ?? "").Trim(), 3000),
                KeyPoints = Lines(c.Course?.KeyPoints, MaxKeyPoints, 300),
                Sections = (c.Course?.Sections ?? new())
                    .Where(s => s != null && (!string.IsNullOrWhiteSpace(s.Title) || !string.IsNullOrWhiteSpace(s.Content)))
                    .Take(MaxSections)
                    .Select(s => new TutorSessionMaterialSection
                    {
                        Title = Clip((s.Title ?? "").Trim(), 200),
                        Content = Clip((s.Content ?? "").Trim(), 6000),
                        Example = NullIfEmpty(Clip((s.Example ?? "").Trim(), 3000)),
                    }).ToList(),
            },
        };
        if (string.IsNullOrWhiteSpace(result.Title))
            result.Title = Clip(b != null && b.Topics.Count > 0 ? string.Join(", ", b.Topics.Select(t => t.Label)) : b?.Subject ?? "Support de séance", 200);

        var qn = 0;
        foreach (var q in (c.Quiz ?? new()).Where(q => q != null && !string.IsNullOrWhiteSpace(q.Text)))
        {
            if (result.Quiz.Count >= MaxQuiz) break;
            var options = (q.Options ?? new()).Where(o => o != null && !string.IsNullOrWhiteSpace(o.Text)).Take(6).ToList();
            if (options.Count < 2) continue;
            var ids = "abcdef";
            var mapped = options.Select((o, i) => new { Old = (o.Id ?? "").Trim(), New = ids[i].ToString(), Text = Clip(o.Text.Trim(), 300) }).ToList();
            var correctOld = (q.CorrectOptionId ?? "").Trim();
            var correct = mapped.FirstOrDefault(o => o.Old == correctOld)?.New
                          ?? mapped.FirstOrDefault(o => string.Equals(o.New, correctOld, StringComparison.OrdinalIgnoreCase))?.New;
            if (correct == null) continue; // une question sans bonne réponse identifiable est écartée
            result.Quiz.Add(new TutorSessionMaterialQuizQuestion
            {
                Id = $"q{++qn}",
                Text = Clip(q.Text.Trim(), 1000),
                Options = mapped.Select(o => new TutorSessionMaterialQuizOption { Id = o.New, Text = o.Text }).ToList(),
                CorrectOptionId = correct,
                Explanation = Clip((q.Explanation ?? "").Trim(), 1500),
            });
        }

        var en = 0;
        foreach (var e in (c.Exercises ?? new()).Where(e => e != null && !string.IsNullOrWhiteSpace(e.Statement)))
        {
            if (result.Exercises.Count >= MaxExercises) break;
            var difficulty = (e.Difficulty ?? "").Trim().ToLowerInvariant();
            result.Exercises.Add(new TutorSessionMaterialExercise
            {
                Id = $"e{++en}",
                Title = Clip((e.Title ?? $"Exercice {en}").Trim(), 200),
                Statement = Clip(e.Statement.Trim(), 4000),
                Difficulty = difficulty is "facile" or "moyen" or "difficile" ? difficulty : "moyen",
                Hints = Lines(e.Hints, MaxHints, 500),
                Solution = Clip((e.Solution ?? "").Trim(), 6000),
            });
        }
        return result;
    }

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private static string? DisplayName(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email?.Split('@')[0]);
}
