using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Backend.Services.Entitlements;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

public sealed class LicensePurchaseRequest
{
    public int StudentSeats { get; set; }
    /// <summary>year (année scolaire) ou quarter.</summary>
    public string? Term { get; set; }
    public string? PromoCode { get; set; }
    public bool AutoRenew { get; set; }
}

public sealed class SeatAssignRequest
{
    public int UserId { get; set; }
    /// <summary>student ou teacher.</summary>
    public string Kind { get; set; } = SeatKinds.Student;
}

/// <summary>
/// Licence de l'établissement du compte connecté (lot 9.7b étape 4, étude 2.3,
/// décision 18.21) : licence et sièges, devis, souscription et renouvellement
/// par le portefeuille de l'établissement, essai par promotion, budget WinAI.
/// </summary>
[ApiController]
[Route("api/institution/license")]
[Authorize(Roles = "institution,admin")]
public class InstitutionLicenseController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly InstitutionLicenseService _licenses;

    public InstitutionLicenseController(ApplicationDbContext db, InstitutionLicenseService licenses)
    {
        _db = db;
        _licenses = licenses;
    }

    private async Task<int?> InstitutionIdAsync() =>
        await _db.Users.AsNoTracking().Where(u => u.Id == User.GetUserId()).Select(u => u.InstitutionId).FirstOrDefaultAsync();

    private IActionResult Fail(LicenseException ex) => StatusCode(ex.StatusCode, new { error = ex.Message });

    /// <summary>Licence en cours, sièges attribués (élèves et enseignants), budget WinAI utilisé (en pourcentage).</summary>
    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        var now = DateTime.UtcNow;
        var license = await _licenses.CurrentAsync(institutionId, now);
        var everHad = await _db.InstitutionLicenses.AnyAsync(l => l.InstitutionId == institutionId);
        if (license == null)
            return Ok(new { license = (object?)null, trialAvailable = !everHad, grid = Grid() });
        var seatUsers = license.Seats.Where(s => s.ReleasedAt == null).Select(s => s.UserId).ToList();
        var names = await _db.Users.AsNoTracking().Where(u => seatUsers.Contains(u.Id))
            .Select(u => new { u.Id, Name = ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Trim(), u.Email })
            .ToDictionaryAsync(u => u.Id);
        var (budget, used) = await _licenses.AiBudgetAsync(institutionId);
        var released = license.Seats.Count(s => s.ReleasedAt != null);
        return Ok(new
        {
            license = new
            {
                license.Id, license.Tier, license.Status, license.Term, license.StartDate, license.EndDate, license.GraceUntil,
                license.StudentSeats, license.TeacherSeats, license.PricePerSeatTtc, license.TotalPriceTtc, license.IsNegotiated,
                license.AutoRenew, license.CancelAtPeriodEnd,
                studentSeatsUsed = license.Seats.Count(s => s.Kind == SeatKinds.Student && s.ReleasedAt == null),
                teacherSeatsUsed = license.Seats.Count(s => s.Kind == SeatKinds.Teacher && s.ReleasedAt == null),
                seatsReleased = released,
                aiBudgetUsedPercent = budget <= 0 ? 0 : (int)Math.Min(100, Math.Round(100.0 * used / budget)),
            },
            seats = license.Seats.Where(s => s.ReleasedAt == null).OrderBy(s => s.Kind).ThenBy(s => s.AssignedAt).Select(s => new
            {
                s.Id, s.Kind, s.UserId, s.AssignedAt,
                name = names.TryGetValue(s.UserId, out var n) ? (string.IsNullOrWhiteSpace(n.Name) ? n.Email : n.Name) : null,
            }),
            trialAvailable = false,
            grid = Grid(),
        });
    }

    private static object Grid() => new
    {
        minSeats = LicensePolicy.MinStudentSeats,
        studentsPerTeacherSeat = LicensePolicy.StudentsPerTeacherSeat,
        minTeacherSeats = LicensePolicy.MinTeacherSeats,
        tiers = new[]
        {
            new { tier = LicenseTiers.School, fromSeats = 20, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(20) },
            new { tier = LicenseTiers.Establishment, fromSeats = 200, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(200) },
            new { tier = LicenseTiers.Network, fromSeats = 1000, yearlyPerSeatTtc = LicensePolicy.YearlyPricePerSeat(1000) },
        },
        trialDays = LicensePolicy.DefaultTrialDays,
        trialSeats = LicensePolicy.TrialSeats,
    };

    [HttpGet("quote")]
    public async Task<IActionResult> Quote([FromQuery] int students, [FromQuery] string? term = null, [FromQuery] string? promoCode = null)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        try { return Ok(await _licenses.QuoteAsync(User.GetUserId(), institutionId, students, term, promoCode)); }
        catch (LicenseException ex) { return Fail(ex); }
    }

    [HttpPost("purchase")]
    public async Task<IActionResult> Purchase([FromBody] LicensePurchaseRequest req)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        try
        {
            var l = await _licenses.PurchaseAsync(User.GetUserId(), institutionId, req.StudentSeats, req.Term, req.PromoCode, req.AutoRenew);
            return Ok(new { id = l.Id, endDate = l.EndDate, amount = l.TotalPriceTtc, message = $"Licence active jusqu'au {l.EndDate:dd/MM/yyyy}." });
        }
        catch (LicenseException ex) { return Fail(ex); }
    }

    [HttpPost("trial")]
    public async Task<IActionResult> Trial([FromBody] LicensePurchaseRequest? req)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        try
        {
            var l = await _licenses.StartTrialAsync(User.GetUserId(), institutionId, req?.PromoCode);
            return Ok(new { id = l.Id, endDate = l.EndDate, seats = l.StudentSeats, message = $"Licence d'essai ouverte jusqu'au {l.EndDate:dd/MM/yyyy} ({l.StudentSeats} sièges)." });
        }
        catch (LicenseException ex) { return Fail(ex); }
    }

    [HttpPost("seats")]
    public async Task<IActionResult> Assign([FromBody] SeatAssignRequest req)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        try { var seat = await _licenses.AssignSeatAsync(institutionId, req.Kind, req.UserId, User.GetUserId()); return Ok(new { seat.Id }); }
        catch (LicenseException ex) { return Fail(ex); }
    }

    [HttpDelete("seats/{seatId:int}")]
    public async Task<IActionResult> Release(int seatId)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        try { await _licenses.ReleaseSeatAsync(institutionId, seatId); return Ok(new { success = true }); }
        catch (LicenseException ex) { return Fail(ex); }
    }

    [HttpPut("auto-renew")]
    public async Task<IActionResult> AutoRenew([FromBody] AutoRenewRequest req)
    {
        var iid = await InstitutionIdAsync();
        if (iid is not int institutionId) return NotFound(new { error = "Compte non rattaché à un établissement." });
        var license = await _licenses.CurrentAsync(institutionId, DateTime.UtcNow);
        if (license == null || license.Status == LicenseStatuses.Trialing) return NotFound(new { error = "Aucune licence payée en cours." });
        license.AutoRenew = req.Enabled;
        license.CancelAtPeriodEnd = false;
        license.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return Ok(new { success = true, autoRenew = license.AutoRenew });
    }
}

public sealed class AdminLicenseRequest
{
    public int InstitutionId { get; set; }
    public int StudentSeats { get; set; }
    public int? TeacherSeats { get; set; }
    public decimal PricePerSeatTtc { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
    public bool ChargeWallet { get; set; }
    public string? Notes { get; set; }
    public string? Reason { get; set; }
}

/// <summary>Licences vues et créées par l'administration (prix négociés), rôle « gestion des abonnements » compris.</summary>
[ApiController]
[Route("api/admin/licenses")]
[Authorize(Policy = AuthorizationPolicies.SubscriptionManagement)]
public class AdminLicensesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly InstitutionLicenseService _licenses;
    private readonly IAdminAuditService _audit;

    public AdminLicensesController(ApplicationDbContext db, InstitutionLicenseService licenses, IAdminAuditService audit)
    {
        _db = db;
        _licenses = licenses;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List()
    {
        var rows = await _db.InstitutionLicenses.AsNoTracking().OrderByDescending(l => l.CreatedAt).Take(500)
            .Select(l => new
            {
                l.Id, l.InstitutionId, institution = l.Institution != null ? l.Institution.Name : null, l.Tier, l.Status, l.Term,
                l.StartDate, l.EndDate, l.StudentSeats, l.TeacherSeats, l.PricePerSeatTtc, l.TotalPriceTtc, l.IsNegotiated, l.AutoRenew,
                studentSeatsUsed = l.Seats.Count(s => s.Kind == SeatKinds.Student && s.ReleasedAt == null),
                teacherSeatsUsed = l.Seats.Count(s => s.Kind == SeatKinds.Teacher && s.ReleasedAt == null),
            }).ToListAsync();
        var institutions = await _db.Institutions.AsNoTracking().Where(i => !i.IsDeleted).OrderBy(i => i.Name)
            .Select(i => new { i.Id, i.Name }).ToListAsync();
        return Ok(new { licenses = rows, institutions });
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] AdminLicenseRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Reason) || req.Reason.Trim().Length < 3) return BadRequest(new { error = "Motif obligatoire." });
        try
        {
            var l = await _licenses.AdminCreateAsync(User.GetUserId(), req.InstitutionId, req.StudentSeats, req.TeacherSeats, req.PricePerSeatTtc,
                req.StartDate, req.EndDate, req.ChargeWallet, req.Notes);
            await this.AuditAsync(_audit, AdminAuditActions.LicenseCreated, "InstitutionLicense", l.Id, $"établissement {l.InstitutionId}",
                after: new { l.StudentSeats, l.TeacherSeats, l.PricePerSeatTtc, l.StartDate, l.EndDate, req.ChargeWallet }, reason: req.Reason);
            return Ok(new { id = l.Id });
        }
        catch (LicenseException ex) { return StatusCode(ex.StatusCode, new { error = ex.Message }); }
    }

    /// <summary>Met fin à une licence (sièges libérés de fait), avec motif.</summary>
    [HttpPost("{id:int}/end")]
    public async Task<IActionResult> End(int id, [FromBody] AdminReasonBody body)
    {
        if (string.IsNullOrWhiteSpace(body?.Reason) || body.Reason.Trim().Length < 3) return BadRequest(new { error = "Motif obligatoire." });
        var l = await _db.InstitutionLicenses.FirstOrDefaultAsync(x => x.Id == id);
        if (l == null) return NotFound(new { error = "Licence introuvable." });
        l.Status = LicenseStatuses.Cancelled;
        l.EndDate = DateTime.UtcNow;
        l.UpdatedAt = DateTime.UtcNow;
        this.AuditAdd(_audit, AdminAuditActions.LicenseEnded, "InstitutionLicense", id, $"établissement {l.InstitutionId}", reason: body.Reason);
        await _db.SaveChangesAsync();
        EntitlementService.InvalidateAll();
        return Ok(new { success = true });
    }
}
