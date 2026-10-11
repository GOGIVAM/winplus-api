using Backend.Data;
using Backend.Models.DTOs;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>Réservation refusée faute de lien vérifié entre le payeur et un élève (lot 9.3).</summary>
public sealed class TutorBookingForbiddenException : InvalidOperationException
{
    public TutorBookingForbiddenException(string message) : base(message) { }
}

/// <summary>
/// Payeur et participants d'une réservation, après vérification de tous les
/// liens (lot 9.3, suivi 17.4).
/// </summary>
/// <param name="PayerType">student | parent | institution | study_group.</param>
/// <param name="PayerUserId">Portefeuille débité et crédité des remboursements.</param>
/// <param name="BookedByUserId">Compte qui réserve.</param>
/// <param name="InstitutionId">Institution payeuse (PayerType = institution).</param>
/// <param name="StudyGroupId">Groupe d'étude (PayerType = study_group).</param>
/// <param name="ParticipantIds">Élèves participants, sans doublon, l'élève principal en premier.</param>
public sealed record TutorBookingParties(
    string PayerType,
    int PayerUserId,
    int BookedByUserId,
    int? InstitutionId,
    int? StudyGroupId,
    IReadOnlyList<int> ParticipantIds,
    int? AssignmentId);

/// <summary>
/// Contrôles d'autorisation de la réservation pour autrui (lot 9.3). Toujours
/// côté serveur, à partir du rôle réel du compte et des liens en base ; le
/// client ne fait que proposer. Règles :
/// <list type="bullet">
///   <item><b>élève</b> : pour lui-même uniquement ;</item>
///   <item><b>parent</b> : pour ses enfants liés par un <see cref="ParentStudentLink"/>
///   au statut <c>accepted</c> (jamais pending ni rejected), payé par le
///   portefeuille parent ou Mobile Money ;</item>
///   <item><b>institution</b> : pour ses élèves rattachés et actifs
///   (<see cref="InstitutionStudent"/>, même InstitutionId que le compte),
///   payé par le portefeuille institution ou Mobile Money ;</item>
///   <item><b>groupe d'étude</b> : un membre réserve pour des membres du même
///   groupe actif ; c'est lui qui paie (son portefeuille ou Mobile Money) et
///   qui reçoit les remboursements ;</item>
///   <item>avec une attribution : elle doit être active, porter sur ce tuteur,
///   et chaque participant doit en être bénéficiaire.</item>
/// </list>
/// Aucun élève n'est jamais ajouté à une séance sans lien vérifié avec le payeur.
/// </summary>
public class TutorBookingPartyResolver
{
    public const int MaxParticipants = TutorPricing.MaxGroupSizeCap;

    private readonly ApplicationDbContext _db;

    public TutorBookingPartyResolver(ApplicationDbContext db) => _db = db;

    private sealed record Actor(int Id, string Role, int? InstitutionId);

    private async Task<Actor> ActorAsync(int userId)
    {
        var u = await _db.Users.AsNoTracking().Where(x => x.Id == userId)
            .Select(x => new { x.Id, x.Role, x.InstitutionId, x.IsActive, x.IsDeleted })
            .FirstOrDefaultAsync()
            ?? throw new TutorBookingForbiddenException("Compte introuvable.");
        if (!u.IsActive || u.IsDeleted)
            throw new TutorBookingForbiddenException("Ce compte ne peut pas réserver (compte suspendu ou supprimé).");
        return new Actor(u.Id, (u.Role ?? "").Trim().ToLowerInvariant(), u.InstitutionId);
    }

    /// <summary>Type de payeur par défaut selon le rôle (un parent réserve en tant que parent, etc.).</summary>
    public static string DefaultPayerType(string role) => role switch
    {
        "parent" => TutorPayerTypes.Parent,
        "institution" => TutorPayerTypes.Institution,
        _ => TutorPayerTypes.Student,
    };

    public async Task<TutorBookingParties> ResolveAsync(int actingUserId, string? requestedPayerType, IEnumerable<int>? requestedParticipants,
        int? studyGroupId, int? assignmentId, int tutorProfileId)
    {
        var actor = await ActorAsync(actingUserId);
        var payerType = string.IsNullOrWhiteSpace(requestedPayerType)
            ? (studyGroupId.HasValue ? TutorPayerTypes.StudyGroup : DefaultPayerType(actor.Role))
            : requestedPayerType.Trim().ToLowerInvariant();
        if (!TutorPayerTypes.All.Contains(payerType))
            throw new TutorBookingForbiddenException("Type de payeur invalide.");

        var participants = (requestedParticipants ?? Enumerable.Empty<int>()).Where(id => id > 0).Distinct().ToList();
        if (participants.Count > MaxParticipants)
            throw new TutorBookingForbiddenException($"Une séance compte au plus {MaxParticipants} élèves.");

        TutorBookingParties parties;
        switch (payerType)
        {
            case TutorPayerTypes.Student:
                // Comportement d'avant le 9.3 conservé : tout compte réserve
                // pour lui-même, sauf parent et institution qui réservent pour
                // leurs élèves.
                if (actor.Role is "parent" or "institution")
                    throw new TutorBookingForbiddenException("Un compte parent ou institution réserve pour ses élèves, pas pour lui-même.");
                if (participants.Count == 0) participants.Add(actor.Id);
                if (participants.Count != 1 || participants[0] != actor.Id)
                    throw new TutorBookingForbiddenException("Un élève réserve pour lui-même ; pour réserver avec d'autres élèves, passe par un groupe d'étude.");
                parties = new(TutorPayerTypes.Student, actor.Id, actor.Id, null, null, participants, null);
                break;

            case TutorPayerTypes.Parent:
            {
                if (actor.Role != "parent")
                    throw new TutorBookingForbiddenException("Seul un compte parent peut réserver pour ses enfants.");
                if (participants.Count == 0)
                    throw new TutorBookingForbiddenException("Choisis au moins un enfant.");
                var linked = await _db.ParentStudentLinks.AsNoTracking()
                    .Where(l => l.ParentId == actor.Id && l.Status == "accepted" && participants.Contains(l.StudentId))
                    .Select(l => l.StudentId).Distinct().ToListAsync();
                if (participants.Any(p => !linked.Contains(p)))
                    throw new TutorBookingForbiddenException("Tu ne peux réserver que pour tes enfants dont le lien est accepté.");
                parties = new(TutorPayerTypes.Parent, actor.Id, actor.Id, null, null, participants, null);
                break;
            }

            case TutorPayerTypes.Institution:
            {
                if (actor.Role != "institution" || actor.InstitutionId is not int institutionId)
                    throw new TutorBookingForbiddenException("Seul un compte institution rattaché à un établissement peut réserver pour ses élèves.");
                if (participants.Count == 0)
                    throw new TutorBookingForbiddenException("Choisis au moins un élève.");
                var attached = await InstitutionStudentIdsAsync(institutionId, participants);
                if (participants.Any(p => !attached.Contains(p)))
                    throw new TutorBookingForbiddenException("Tu ne peux réserver que pour des élèves rattachés et actifs dans ton institution.");
                var holder = await InstitutionWallet.ResolveHolderAsync(_db, institutionId, actor.Id);
                parties = new(TutorPayerTypes.Institution, holder, actor.Id, institutionId, null, participants, null);
                break;
            }

            default: // study_group
            {
                if (studyGroupId is not int groupId)
                    throw new TutorBookingForbiddenException("Choisis le groupe d'étude.");
                var group = await _db.StudyGroups.AsNoTracking().Where(g => g.Id == groupId && g.IsActive)
                    .Select(g => new { g.Id, Members = g.Members.Select(m => m.UserId).ToList() })
                    .FirstOrDefaultAsync()
                    ?? throw new TutorBookingForbiddenException("Groupe d'étude introuvable ou inactif.");
                if (!group.Members.Contains(actor.Id))
                    throw new TutorBookingForbiddenException("Seul un membre du groupe peut réserver une séance pour ce groupe.");
                if (participants.Count == 0)
                    throw new TutorBookingForbiddenException("Choisis les membres qui participent.");
                if (participants.Any(p => !group.Members.Contains(p)))
                    throw new TutorBookingForbiddenException("Tous les participants doivent être membres du groupe d'étude.");
                // Le membre qui réserve paie et reçoit les remboursements ; il
                // figure en tête s'il participe.
                if (participants.Remove(actor.Id)) participants.Insert(0, actor.Id);
                parties = new(TutorPayerTypes.StudyGroup, actor.Id, actor.Id, null, groupId, participants, null);
                break;
            }
        }

        // Tous les participants sont des comptes actifs (un lien peut survivre
        // à un compte supprimé).
        var activeCount = await _db.Users.AsNoTracking()
            .CountAsync(u => parties.ParticipantIds.Contains(u.Id) && u.IsActive && !u.IsDeleted);
        if (activeCount != parties.ParticipantIds.Count)
            throw new TutorBookingForbiddenException("Un des élèves choisis n'a plus de compte actif.");

        if (assignmentId is int aid)
            parties = parties with { AssignmentId = await CheckAssignmentAsync(aid, actor, parties, tutorProfileId) };

        return parties;
    }

    private async Task<int> CheckAssignmentAsync(int assignmentId, Actor actor, TutorBookingParties parties, int tutorProfileId)
    {
        var a = await _db.TutorAssignments.AsNoTracking().Include(x => x.Beneficiaries)
            .FirstOrDefaultAsync(x => x.Id == assignmentId)
            ?? throw new TutorBookingForbiddenException("Attribution introuvable.");
        if (a.Status != TutorAssignmentStatuses.Active)
            throw new TutorBookingForbiddenException("Cette attribution n'est pas active.");
        if (a.TutorProfileId != tutorProfileId)
            throw new TutorBookingForbiddenException("Cette attribution concerne un autre tuteur.");
        var beneficiaries = a.Beneficiaries.Select(b => b.StudentUserId).ToHashSet();
        if (parties.ParticipantIds.Any(p => !beneficiaries.Contains(p)))
            throw new TutorBookingForbiddenException("Tous les participants doivent être bénéficiaires de l'attribution.");
        var sameSide = a.AssignerType == TutorPayerTypes.Institution
            ? actor.InstitutionId != null && a.InstitutionId == actor.InstitutionId
            : a.AssignedByUserId == actor.Id;
        var beneficiaryForSelf = parties.PayerType == TutorPayerTypes.Student && beneficiaries.Contains(actor.Id);
        if (!sameSide && !beneficiaryForSelf)
            throw new TutorBookingForbiddenException("Cette attribution ne te concerne pas.");
        return a.Id;
    }

    private Task<List<int>> InstitutionStudentIdsAsync(int institutionId, List<int> studentIds) =>
        _db.InstitutionStudents.AsNoTracking()
            .Where(s => s.InstitutionId == institutionId && s.IsActive && studentIds.Contains(s.StudentId))
            .Select(s => s.StudentId).Distinct().ToListAsync();

    // ── Bénéficiaires d'une attribution ─────────────────────────────────

    /// <summary>
    /// Vérifie les bénéficiaires d'une attribution créée par <paramref name="actingUserId"/> :
    /// enfants liés acceptés (parent), élèves actifs de l'institution (institution,
    /// éventuellement toute une classe <paramref name="groupName"/>).
    /// </summary>
    public async Task<(string AssignerType, int? InstitutionId, List<int> StudentIds)> ResolveAssignmentBeneficiariesAsync(
        int actingUserId, IEnumerable<int>? studentIds, string? groupName)
    {
        var actor = await ActorAsync(actingUserId);
        var ids = (studentIds ?? Enumerable.Empty<int>()).Where(i => i > 0).Distinct().ToList();

        if (actor.Role == "parent")
        {
            if (ids.Count == 0) throw new TutorBookingForbiddenException("Choisis au moins un enfant.");
            var linked = await _db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.ParentId == actor.Id && l.Status == "accepted" && ids.Contains(l.StudentId))
                .Select(l => l.StudentId).Distinct().ToListAsync();
            if (ids.Any(i => !linked.Contains(i)))
                throw new TutorBookingForbiddenException("Tu ne peux attribuer un tuteur qu'à tes enfants dont le lien est accepté.");
            return (TutorPayerTypes.Parent, null, ids);
        }

        if (actor.Role == "institution" && actor.InstitutionId is int institutionId)
        {
            if (!string.IsNullOrWhiteSpace(groupName))
            {
                var g = groupName.Trim();
                var classIds = await _db.InstitutionStudents.AsNoTracking()
                    .Where(s => s.InstitutionId == institutionId && s.IsActive && s.GroupName == g)
                    .Select(s => s.StudentId).ToListAsync();
                if (classIds.Count == 0) throw new TutorBookingForbiddenException("Aucun élève actif dans cette classe.");
                ids = ids.Union(classIds).ToList();
            }
            if (ids.Count == 0) throw new TutorBookingForbiddenException("Choisis des élèves ou une classe.");
            if (ids.Count > 200) throw new TutorBookingForbiddenException("Une attribution compte au plus 200 élèves.");
            var attached = await InstitutionStudentIdsAsync(institutionId, ids);
            if (ids.Any(i => !attached.Contains(i)))
                throw new TutorBookingForbiddenException("Tu ne peux attribuer un tuteur qu'à des élèves rattachés et actifs dans ton institution.");
            return (TutorPayerTypes.Institution, institutionId, ids);
        }

        throw new TutorBookingForbiddenException("Seul un parent ou une institution peut attribuer un tuteur.");
    }

    // ── Contexte de réservation (listes proposées aux clients) ──────────

    public async Task<TutorBookingContextDto> GetContextAsync(int userId)
    {
        var actor = await ActorAsync(userId);
        var ctx = new TutorBookingContextDto { Role = actor.Role };

        static string Name(string? first, string? last, string email) =>
            $"{first} {last}".Trim() is { Length: > 0 } n ? n : email.Split('@')[0];

        if (actor.Role == "parent")
        {
            ctx.AllowedPayerTypes.Add(TutorPayerTypes.Parent);
            var children = await _db.ParentStudentLinks.AsNoTracking()
                .Where(l => l.ParentId == actor.Id && l.Status == "accepted" && l.Student != null && !l.Student.IsDeleted)
                .Select(l => new { l.StudentId, l.Student!.FirstName, l.Student.LastName, l.Student.Email, l.Student.AvatarUrl })
                .ToListAsync();
            ctx.Children = children
                .GroupBy(s => s.StudentId).Select(g => g.First())
                .Select(s => new TutorBookingParticipantDto { UserId = s.StudentId, Name = Name(s.FirstName, s.LastName, s.Email), AvatarUrl = s.AvatarUrl })
                .ToList();
        }
        else if (actor.Role == "institution" && actor.InstitutionId is int institutionId)
        {
            ctx.AllowedPayerTypes.Add(TutorPayerTypes.Institution);
            ctx.InstitutionId = institutionId;
            ctx.InstitutionName = await _db.Institutions.AsNoTracking().Where(i => i.Id == institutionId).Select(i => i.Name).FirstOrDefaultAsync();
            var rows = await _db.InstitutionStudents.AsNoTracking()
                .Where(s => s.InstitutionId == institutionId && s.IsActive && s.Student != null && !s.Student.IsDeleted)
                .Select(s => new { s.StudentId, s.GroupName, s.Level, s.Student!.FirstName, s.Student.LastName, s.Student.Email })
                .OrderBy(s => s.GroupName).ThenBy(s => s.LastName)
                .Take(2000)
                .ToListAsync();
            ctx.InstitutionStudents = rows.Select(s => new TutorBookableStudentDto
            {
                UserId = s.StudentId, Name = Name(s.FirstName, s.LastName, s.Email), GroupName = s.GroupName, Level = s.Level,
            }).ToList();
        }
        else
        {
            ctx.AllowedPayerTypes.Add(TutorPayerTypes.Student);
            var groups = await _db.StudyGroups.AsNoTracking()
                .Where(g => g.IsActive && g.Members.Any(m => m.UserId == actor.Id))
                .Select(g => new
                {
                    g.Id, g.Name, g.Subject,
                    Members = g.Members.Where(m => m.User != null && !m.User.IsDeleted)
                        .Select(m => new { m.UserId, m.User!.FirstName, m.User.LastName, m.User.Email, m.User.AvatarUrl }).ToList(),
                })
                .ToListAsync();
            ctx.StudyGroups = groups.Select(g => new TutorBookableStudyGroupDto
            {
                Id = g.Id, Name = g.Name, Subject = g.Subject,
                Members = g.Members.Select(m => new TutorBookingParticipantDto { UserId = m.UserId, Name = Name(m.FirstName, m.LastName, m.Email), AvatarUrl = m.AvatarUrl }).ToList(),
            }).ToList();
            if (ctx.StudyGroups.Count > 0) ctx.AllowedPayerTypes.Add(TutorPayerTypes.StudyGroup);
        }

        return ctx;
    }
}

/// <summary>
/// Portefeuille institution (lot 9.3, suivi 17.4). Réutilise tel quel le
/// registre du lot 2 : les écritures appartiennent au compte titulaire
/// (<see cref="Institution.WalletOwnerUserId"/>), avec OwnerType
/// « institution ». Tous les comptes de l'institution passent par ce titulaire
/// pour le solde, l'historique, la recharge et les débits ; les remboursements
/// de tutorat y reviennent (TutorBooking.PayerUserId = titulaire).
/// </summary>
public static class InstitutionWallet
{
    /// <summary>
    /// Titulaire du portefeuille de l'institution, fixé au premier usage sur
    /// <paramref name="actingUserId"/> (compte institution de cette institution).
    /// </summary>
    public static async Task<int> ResolveHolderAsync(ApplicationDbContext db, int institutionId, int actingUserId)
    {
        var institution = await db.Institutions.FirstOrDefaultAsync(i => i.Id == institutionId && !i.IsDeleted)
            ?? throw new TutorBookingForbiddenException("Institution introuvable.");
        if (institution.WalletOwnerUserId is int holder) return holder;

        institution.WalletOwnerUserId = actingUserId;
        institution.UpdatedAt = DateTime.UtcNow;
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Premier usage concurrent par deux comptes : l'autre a gagné.
            db.Entry(institution).State = EntityState.Detached;
            var current = await db.Institutions.AsNoTracking().Where(i => i.Id == institutionId).Select(i => i.WalletOwnerUserId).FirstOrDefaultAsync();
            if (current is int other) return other;
            throw;
        }
        return actingUserId;
    }

    /// <summary>
    /// Portefeuille à lire ou recharger pour <paramref name="userId"/> : le
    /// titulaire de son institution pour un compte institution rattaché, sinon
    /// son propre portefeuille.
    /// </summary>
    public static async Task<int> ResolveWalletOwnerAsync(ApplicationDbContext db, int userId)
    {
        var u = await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => new { x.Role, x.InstitutionId }).FirstOrDefaultAsync();
        if (u == null || !string.Equals(u.Role, "institution", StringComparison.OrdinalIgnoreCase) || u.InstitutionId is not int institutionId)
            return userId;
        if (!await db.Institutions.AsNoTracking().AnyAsync(i => i.Id == institutionId && !i.IsDeleted))
            return userId;
        return await ResolveHolderAsync(db, institutionId, userId);
    }
}
