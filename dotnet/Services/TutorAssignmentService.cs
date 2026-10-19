using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Attribution d'un tuteur (lot 9.3, suivi 17.4) : un parent à ses enfants,
/// une institution à un groupe ou une classe de ses élèves. Relation suivie
/// (en attente → active ou refusée → terminée), que le tuteur accepte ou
/// refuse. Ne réserve jamais rien : elle propose le tuteur en premier et
/// présélectionne les bénéficiaires à la réservation (TutorBookingPartyResolver
/// revérifie tout à la création de la réservation).
/// </summary>
public interface ITutorAssignmentService
{
    Task<TutorAssignmentDto> CreateAsync(int actingUserId, CreateTutorAssignmentRequestDto request);
    /// <summary>Attributions créées par le compte (parent) ou par son institution (comptes institution).</summary>
    Task<List<TutorAssignmentDto>> GetAsAssignerAsync(int userId);
    Task<List<TutorAssignmentDto>> GetAsTutorAsync(int tutorUserId);
    /// <summary>Attributions actives dont l'élève est bénéficiaire.</summary>
    Task<List<TutorAssignmentDto>> GetForStudentAsync(int studentUserId);
    Task<TutorAssignmentDto> AcceptAsync(int tutorUserId, int assignmentId, string? note);
    Task<TutorAssignmentDto> DeclineAsync(int tutorUserId, int assignmentId, string? note);
    Task<TutorAssignmentDto> EndAsync(int userId, int assignmentId);
    Task<TutorBookingContextDto> GetBookingContextAsync(int userId);
}

public class TutorAssignmentService : ITutorAssignmentService
{
    private static readonly string[] OpenStatuses = { TutorAssignmentStatuses.Pending, TutorAssignmentStatuses.Active };

    private readonly ApplicationDbContext _db;
    private readonly INtfyService _ntfy;
    private readonly TutorBookingPartyResolver _parties;

    public TutorAssignmentService(ApplicationDbContext db, INtfyService ntfy)
    {
        _db = db;
        _ntfy = ntfy;
        _parties = new TutorBookingPartyResolver(db);
    }

    private IQueryable<TutorAssignment> WithDetails() => _db.TutorAssignments
        .Include(a => a.TutorProfile).ThenInclude(p => p!.User)
        .Include(a => a.AssignedBy)
        .Include(a => a.Beneficiaries).ThenInclude(b => b.Student);

    private async Task<int?> InstitutionOfAsync(int userId) =>
        await _db.Users.AsNoTracking().Where(u => u.Id == userId && u.Role == "institution").Select(u => u.InstitutionId).FirstOrDefaultAsync();

    public async Task<TutorAssignmentDto> CreateAsync(int actingUserId, CreateTutorAssignmentRequestDto request)
    {
        var tutor = await _db.TutorProfiles.Include(p => p.User)
            .FirstOrDefaultAsync(p => p.UserId == request.TutorUserId)
            ?? throw new InvalidOperationException("Ce répétiteur n'existe pas.");
        if (!tutor.IsActive)
            throw new InvalidOperationException("Ce répétiteur n'est pas actif pour l'instant.");

        var (assignerType, institutionId, studentIds) =
            await _parties.ResolveAssignmentBeneficiariesAsync(actingUserId, request.StudentUserIds, request.GroupName);

        // Une seule attribution ouverte par tuteur et par élève, côté attribuant.
        var overlap = await _db.TutorAssignments.AsNoTracking()
            .Where(a => a.TutorProfileId == tutor.Id && OpenStatuses.Contains(a.Status)
                     && (assignerType == TutorPayerTypes.Institution ? a.InstitutionId == institutionId : a.AssignedByUserId == actingUserId)
                     && a.Beneficiaries.Any(b => studentIds.Contains(b.StudentUserId)))
            .AnyAsync();
        if (overlap)
            throw new InvalidOperationException("Une attribution est déjà en cours avec ce tuteur pour au moins un de ces élèves.");

        var assignment = new TutorAssignment
        {
            TutorProfileId = tutor.Id,
            AssignedByUserId = actingUserId,
            AssignerType = assignerType,
            InstitutionId = institutionId,
            GroupName = string.IsNullOrWhiteSpace(request.GroupName) ? null : Truncate(request.GroupName.Trim(), 100),
            Subject = string.IsNullOrWhiteSpace(request.Subject) ? null : Truncate(request.Subject.Trim(), 100),
            Note = string.IsNullOrWhiteSpace(request.Note) ? null : Truncate(request.Note.Trim(), 500),
            Status = TutorAssignmentStatuses.Pending,
            Beneficiaries = studentIds.Select(id => new TutorAssignmentBeneficiary { StudentUserId = id }).ToList(),
        };
        _db.TutorAssignments.Add(assignment);
        await _db.SaveChangesAsync();

        // Lot 10.6 : notifications dans la langue du destinataire (User.Locale).
        await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{tutor.UserId}", new("Nouvelle attribution", "New assignment"),
            assignerType == TutorPayerTypes.Institution
                ? new($"Une institution te propose de suivre {studentIds.Count} élève(s){(assignment.GroupName != null ? $" ({assignment.GroupName})" : "")}. Accepte ou refuse depuis tes demandes.",
                      $"A school is asking you to tutor {studentIds.Count} student(s){(assignment.GroupName != null ? $" ({assignment.GroupName})" : "")}. Accept or decline from your requests.")
                : new($"Un parent te propose de suivre {(studentIds.Count > 1 ? $"ses {studentIds.Count} enfants" : "son enfant")}. Accepte ou refuse depuis tes demandes.",
                      $"A parent is asking you to tutor {(studentIds.Count > 1 ? $"their {studentIds.Count} children" : "their child")}. Accept or decline from your requests."),
            userId: tutor.UserId, type: "TutorAssignment");

        return (await MapAsync(new[] { await WithDetails().FirstAsync(a => a.Id == assignment.Id) }))[0];
    }

    public async Task<List<TutorAssignmentDto>> GetAsAssignerAsync(int userId)
    {
        var institutionId = await InstitutionOfAsync(userId);
        var query = institutionId is int iid
            ? WithDetails().Where(a => a.AssignerType == TutorPayerTypes.Institution && a.InstitutionId == iid)
            : WithDetails().Where(a => a.AssignerType == TutorPayerTypes.Parent && a.AssignedByUserId == userId);
        return await MapAsync(await query.OrderByDescending(a => a.CreatedAt).ToListAsync());
    }

    public async Task<List<TutorAssignmentDto>> GetAsTutorAsync(int tutorUserId) =>
        await MapAsync(await WithDetails().Where(a => a.TutorProfile!.UserId == tutorUserId)
            .OrderBy(a => a.Status == TutorAssignmentStatuses.Pending ? 0 : a.Status == TutorAssignmentStatuses.Active ? 1 : 2)
            .ThenByDescending(a => a.CreatedAt).ToListAsync());

    public async Task<List<TutorAssignmentDto>> GetForStudentAsync(int studentUserId) =>
        await MapAsync(await WithDetails()
            .Where(a => a.Status == TutorAssignmentStatuses.Active && a.Beneficiaries.Any(b => b.StudentUserId == studentUserId))
            .OrderByDescending(a => a.RespondedAt).ToListAsync());

    private async Task<TutorAssignment> LoadForTutorAsync(int tutorUserId, int id)
    {
        var a = await WithDetails().FirstOrDefaultAsync(x => x.Id == id)
            ?? throw new InvalidOperationException("Attribution introuvable.");
        if (a.TutorProfile?.UserId != tutorUserId)
            throw new InvalidOperationException("Cette attribution ne te concerne pas.");
        return a;
    }

    public async Task<TutorAssignmentDto> AcceptAsync(int tutorUserId, int assignmentId, string? note) =>
        await RespondAsync(tutorUserId, assignmentId, note, TutorAssignmentStatuses.Active);

    public async Task<TutorAssignmentDto> DeclineAsync(int tutorUserId, int assignmentId, string? note) =>
        await RespondAsync(tutorUserId, assignmentId, note, TutorAssignmentStatuses.Declined);

    private async Task<TutorAssignmentDto> RespondAsync(int tutorUserId, int assignmentId, string? note, string status)
    {
        var a = await LoadForTutorAsync(tutorUserId, assignmentId);
        if (a.Status != TutorAssignmentStatuses.Pending)
            throw new InvalidOperationException("Cette attribution n'est plus en attente.");
        a.Status = status;
        a.RespondedAt = DateTime.UtcNow;
        a.ResponseNote = string.IsNullOrWhiteSpace(note) ? null : Truncate(note.Trim(), 300);
        await _db.SaveChangesAsync();

        var accepted = status == TutorAssignmentStatuses.Active;
        await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{a.AssignedByUserId}",
            accepted ? new("Attribution acceptée", "Assignment accepted") : new("Attribution refusée", "Assignment declined"),
            accepted
                ? new("Le répétiteur a accepté de suivre tes élèves. Tu peux réserver une séance en un clic depuis Mes attributions.",
                      "The tutor has agreed to tutor your students. You can book a session in one click from My assignments.")
                : new("Le répétiteur a décliné l'attribution. Tu peux en choisir un autre.",
                      "The tutor has declined the assignment. You can choose another one."),
            userId: a.AssignedByUserId, type: "TutorAssignment");
        if (accepted)
            foreach (var b in a.Beneficiaries)
                await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{b.StudentUserId}", new("Un répétiteur t'a été attribué", "A tutor has been assigned to you"),
                    new($"{TutorName(a)} te suit désormais{(a.Subject != null ? $" en {a.Subject}" : "")}. Il apparaît en premier dans ta recherche de répétiteurs.",
                        $"{TutorName(a)} is now your tutor{(a.Subject != null ? $" in {a.Subject}" : "")}. They appear first in your tutor search."),
                    userId: b.StudentUserId, type: "TutorAssignment");

        return (await MapAsync(new[] { a }))[0];
    }

    public async Task<TutorAssignmentDto> EndAsync(int userId, int assignmentId)
    {
        var a = await WithDetails().FirstOrDefaultAsync(x => x.Id == assignmentId)
            ?? throw new InvalidOperationException("Attribution introuvable.");
        var institutionId = await InstitutionOfAsync(userId);
        var isAssignerSide = a.AssignerType == TutorPayerTypes.Institution
            ? institutionId != null && a.InstitutionId == institutionId
            : a.AssignedByUserId == userId;
        var isTutor = a.TutorProfile?.UserId == userId;
        if (!isAssignerSide && !isTutor)
            throw new InvalidOperationException("Cette attribution ne te concerne pas.");
        if (!OpenStatuses.Contains(a.Status))
            throw new InvalidOperationException("Cette attribution est déjà terminée.");

        a.Status = TutorAssignmentStatuses.Ended;
        a.EndedAt = DateTime.UtcNow;
        a.EndedByUserId = userId;
        await _db.SaveChangesAsync();

        var notified = isTutor ? a.AssignedByUserId : a.TutorProfile?.UserId;
        if (notified is int n)
            await _ntfy.PublishLocalizedAsync(_db, $"winplus-user-{n}", new("Attribution terminée", "Assignment ended"),
                new("Une attribution de répétiteur a pris fin. Les séances déjà réservées ne sont pas modifiées.",
                    "A tutor assignment has ended. Sessions already booked are not affected."), userId: n, type: "TutorAssignment");
        return (await MapAsync(new[] { a }))[0];
    }

    public async Task<TutorBookingContextDto> GetBookingContextAsync(int userId)
    {
        var ctx = await _parties.GetContextAsync(userId);
        ctx.ActiveAssignments = ctx.Role is "parent" or "institution"
            ? (await GetAsAssignerAsync(userId)).Where(a => a.Status == TutorAssignmentStatuses.Active).ToList()
            : await GetForStudentAsync(userId);
        return ctx;
    }

    private static string TutorName(TutorAssignment a) =>
        a.TutorProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : "Ton répétiteur";

    private static string? Name(User? u) =>
        u == null ? null : ($"{u.FirstName} {u.LastName}".Trim() is { Length: > 0 } n ? n : u.Email?.Split('@')[0]);

    private async Task<List<TutorAssignmentDto>> MapAsync(IEnumerable<TutorAssignment> list)
    {
        var items = list.ToList();
        var institutionIds = items.Where(a => a.InstitutionId != null).Select(a => a.InstitutionId!.Value).Distinct().ToList();
        var names = institutionIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Institutions.AsNoTracking().IgnoreQueryFilters().Where(i => institutionIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, i => i.Name);
        return items.Select(a => new TutorAssignmentDto
        {
            Id = a.Id,
            TutorUserId = a.TutorProfile?.UserId ?? 0,
            TutorName = a.TutorProfile?.User is { } u ? $"{u.FirstName} {u.LastName}".Trim() : null,
            TutorAvatarUrl = a.TutorProfile?.User?.AvatarUrl,
            AssignedByUserId = a.AssignedByUserId,
            AssignedByName = Name(a.AssignedBy),
            AssignerType = a.AssignerType,
            InstitutionId = a.InstitutionId,
            InstitutionName = a.InstitutionId is int iid && names.TryGetValue(iid, out var n) ? n : null,
            GroupName = a.GroupName,
            Subject = a.Subject,
            Note = a.Note,
            Status = a.Status,
            ResponseNote = a.ResponseNote,
            Beneficiaries = a.Beneficiaries.Select(b => new TutorBookingParticipantDto { UserId = b.StudentUserId, Name = Name(b.Student), AvatarUrl = b.Student?.AvatarUrl }).ToList(),
            CreatedAt = a.CreatedAt,
            RespondedAt = a.RespondedAt,
            EndedAt = a.EndedAt,
        }).ToList();
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
