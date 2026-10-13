using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;

namespace Backend.Controllers;

/// <summary>
/// Module 6  Réservation de séances de cours particulier (professeur_complete.md).
/// Distinct de TutorProfileController (déclaration du profil/des disponibilités
/// types) : ce contrôleur gère le cycle de vie réel d'une réservation datée,
/// paiement compris.
/// </summary>
[ApiController]
[Route("api/tutor-bookings")]
[Authorize]
[Produces("application/json")]
public class TutorBookingController : ControllerBase
{
    private readonly ITutorBookingService _service;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IAdminAuditService? _audit;
    private readonly Backend.Data.ApplicationDbContext? _db;

    public TutorBookingController(ITutorBookingService service, IHttpClientFactory httpClientFactory,
        IAdminAuditService? audit = null, Backend.Data.ApplicationDbContext? db = null)
    {
        _service = service;
        _httpClientFactory = httpClientFactory;
        _audit = audit;
        _db = db;
    }

    /// <summary>
    /// Réserve une séance et la paie : Mobile Money (paiement NotchPay initié)
    /// ou solde du portefeuille (lot 9.2, débit atomique avec la réservation,
    /// 400 si solde insuffisant).
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<TutorBookingCreatedResponseDto>> Create([FromBody] CreateTutorBookingRequestDto request)
    {
        try
        {
            return Ok(await _service.CreateBookingAsync(User.GetUserId(), request));
        }
        catch (TutorBookingForbiddenException ex)
        {
            // Lot 9.3 : élève sans lien vérifié avec le payeur, rôle incompatible.
            return StatusCode(403, new { message = ex.Message });
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>
    /// Devis d'une séance (lot 9.1, suivi 17.3) : le client affiche ce prix
    /// avant paiement, jamais un montant calculé de son côté. Recalculé de
    /// toute façon à la création de la réservation.
    /// </summary>
    [HttpPost("quote")]
    [AllowAnonymous]
    public async Task<ActionResult<TutorPriceQuoteDto>> Quote([FromBody] TutorPriceQuoteRequestDto request)
    {
        try
        {
            return Ok(await _service.GetQuoteAsync(request));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Statut d'une réservation (polling du paiement depuis le front).</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<TutorBookingDto>> GetById(int id)
    {
        try
        {
            return Ok(await _service.GetByIdAsync(User.GetUserId(), id));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>Mes réservations côté élève ou payeur : participant, payeur, réservant, ou institution payeuse (lot 9.3).</summary>
    [HttpGet("mine")]
    public async Task<ActionResult<List<TutorBookingDto>>> GetMine()
        => Ok(await _service.GetMyBookingsAsStudentAsync(User.GetUserId()));

    /// <summary>Réservations reçues en tant que répétiteur.</summary>
    [HttpGet("tutor/mine")]
    public async Task<ActionResult<List<TutorBookingDto>>> GetTutorMine()
        => Ok(await _service.GetMyBookingsAsTutorAsync(User.GetUserId()));

    /// <summary>Demandes en attente d'acceptation, avec délai restant (US-REP-05).</summary>
    [HttpGet("tutor/pending")]
    public async Task<ActionResult<List<TutorPendingBookingDto>>> GetPending()
        => Ok(await _service.GetPendingRequestsAsync(User.GetUserId()));

    /// <summary>Annule une réservation (élève ou répétiteur concerné uniquement).</summary>
    [HttpPost("{id:int}/cancel")]
    public async Task<ActionResult<TutorBookingDto>> Cancel(int id, [FromBody] CancelTutorBookingRequestDto request)
    {
        try
        {
            return Ok(await _service.CancelBookingAsync(User.GetUserId(), id, request.Reason));
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    /// <summary>Le répétiteur accepte une demande en attente.</summary>
    [HttpPut("{id:int}/confirm")]
    public async Task<ActionResult<TutorBookingDto>> Confirm(int id)
    {
        try { return Ok(await _service.ConfirmBookingAsync(User.GetUserId(), id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Le répétiteur refuse une demande en attente  remboursement déclenché.</summary>
    [HttpPut("{id:int}/decline")]
    public async Task<ActionResult<TutorBookingDto>> Decline(int id, [FromBody] DeclineTutorBookingRequestDto request)
    {
        try { return Ok(await _service.DeclineBookingAsync(User.GetUserId(), id, request.Reason)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Le répétiteur marque la séance effectuée (disponible dès l'heure de fin prévue).</summary>
    [HttpPut("{id:int}/complete")]
    public async Task<ActionResult<TutorBookingDto>> Complete(int id)
    {
        try { return Ok(await _service.CompleteBookingAsync(User.GetUserId(), id)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>L'élève conteste une séance marquée effectuée, dans la fenêtre de contestation (24 h par défaut, TutorBooking:DisputeWindowHours).</summary>
    [HttpPut("{id:int}/dispute")]
    public async Task<ActionResult<TutorBookingDto>> Dispute(int id, [FromBody] DisputeTutorBookingRequestDto request)
    {
        try { return Ok(await _service.DisputeBookingAsync(User.GetUserId(), id, request.Reason)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Calendrier de disponibilité réelle d'un répétiteur pour une semaine (US-ELV-01), fermé automatiquement si son plafond hebdo est atteint.</summary>
    [HttpGet("tutor/{tutorUserId:int}/calendar")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TutorAvailabilityOccurrenceDto>>> GetCalendar(int tutorUserId, [FromQuery] DateOnly weekStart)
    {
        try
        {
            return Ok(await _service.GetAvailabilityCalendarAsync(tutorUserId, weekStart));
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(new { message = ex.Message });
        }
    }

    /// <summary>WinAI génère le compte-rendu structuré de la séance à partir d'une transcription (US-REP-07).</summary>
    [HttpPost("{id:int}/summarize")]
    public async Task<IActionResult> Summarize(int id, [FromBody] GenerateTutorBookingSummaryRequestDto request, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient("FastApiClient");
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/teacher/session-summary")
        {
            Content = System.Net.Http.Json.JsonContent.Create(new { transcript_text = request.TranscriptText })
        };
        var auth = HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) req.Headers.TryAddWithoutValidation("Authorization", auth);
        var res = await client.SendAsync(req, ct);
        return Content(await res.Content.ReadAsStringAsync(ct), "application/json");
    }

    /// <summary>Enregistre le compte-rendu édité par le répétiteur avant envoi à l'élève.</summary>
    [HttpPut("{id:int}/summary")]
    public async Task<ActionResult<TutorBookingDto>> UpdateSummary(int id, [FromBody] UpdateTutorBookingSummaryRequestDto request)
    {
        try { return Ok(await _service.SetSummaryAsync(User.GetUserId(), id, request.SummaryText)); }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>Support WinPlus : liste des réservations actuellement en litige (US-REP-09).</summary>
    [HttpGet("disputes")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<List<TutorBookingDto>>> GetDisputes()
        => Ok(await _service.GetDisputedBookingsAsync());

    /// <summary>Support WinPlus : tranche un litige (remboursement total/partiel ou libération des fonds).</summary>
    [HttpPost("{id:int}/resolve-dispute")]
    [Authorize(Roles = "admin")]
    public async Task<ActionResult<TutorBookingDto>> ResolveDispute(int id, [FromBody] ResolveTutorBookingDisputeRequestDto request)
    {
        try
        {
            var result = await _service.ResolveDisputeAsync(User.GetUserId(), id, request.Resolution, request.Note, request.AmountXaf);
            // Lot 9.7a (18.3) : la résolution d'un litige est inscrite au journal d'audit.
            if (_audit != null)
                await this.AuditAsync(_audit, Backend.Models.Entities.AdminAuditActions.TutorDisputeResolved, "TutorBooking", id,
                    after: new { request.Resolution, request.AmountXaf }, reason: request.Note);
            return Ok(result);
        }
        catch (InvalidOperationException ex) { return BadRequest(new { message = ex.Message }); }
    }

    /// <summary>
    /// Lot 9.7a : historique des litiges tranchés (plus récents d'abord, paginé),
    /// pour que la décision, son auteur et son motif restent consultables.
    /// </summary>
    [HttpGet("disputes/history")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> GetDisputeHistory([FromQuery] int page = 1, [FromQuery] int pageSize = 25)
    {
        if (_db == null) return StatusCode(500);
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);
        var query = Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.AsNoTracking(_db.TutorBookings)
            .Where(b => b.DisputeResolvedAt != null);
        var total = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(query);
        var rows = await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.ToListAsync(
            query.OrderByDescending(b => b.DisputeResolvedAt).Skip((page - 1) * pageSize).Take(pageSize)
                .Select(b => new
                {
                    id = b.Id,
                    subject = b.Subject,
                    sessionDate = b.SessionDate,
                    priceXaf = b.PriceXaf,
                    disputeReason = b.DisputeReason,
                    resolution = b.DisputeResolution,
                    note = b.DisputeResolutionNote,
                    refundAmountXaf = b.RefundAmountXaf,
                    resolvedAt = b.DisputeResolvedAt,
                    resolvedBy = _db.Users.Where(u => u.Id == b.DisputeResolvedByUserId).Select(u => u.Email).FirstOrDefault(),
                    tutorName = b.TutorProfile != null && b.TutorProfile.User != null
                        ? (b.TutorProfile.User.FirstName + " " + b.TutorProfile.User.LastName).Trim() : null,
                }));
        return Ok(new { items = rows, total, page, pageSize });
    }
}
