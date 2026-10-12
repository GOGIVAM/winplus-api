using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

/// <summary>
/// Engagement mensuel de tutorat (lot 9.4, suivi 17.7). Les actions sur une
/// séance précise (marquer effectuée, contester, annuler une séance seule)
/// passent par <see cref="TutorBookingController"/> : chaque séance garde son
/// propre cycle.
/// </summary>
[ApiController]
[Route("api/tutor-engagements")]
[Authorize]
[Produces("application/json")]
public class TutorEngagementController : ControllerBase
{
    private readonly ITutorEngagementService _service;

    public TutorEngagementController(ITutorEngagementService service) => _service = service;

    private ActionResult Fail(Exception ex) => ex switch
    {
        TutorBookingForbiddenException f => StatusCode(403, new { message = f.Message }),
        TutorEngagementConflictException c => Conflict(new { message = c.Message, conflicts = c.Conflicts }),
        InsufficientWalletBalanceException w => BadRequest(new { message = w.Message, availableXaf = w.AvailableXaf, requiredXaf = w.RequiredXaf }),
        KeyNotFoundException n => NotFound(new { message = n.Message }),
        InvalidOperationException i => BadRequest(new { message = i.Message }),
        _ => throw ex,
    };

    private async Task<ActionResult<T>> Run<T>(Func<Task<T>> action)
    {
        try { return Ok(await action()); }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException) { return Fail(ex); }
    }

    /// <summary>Devis mensuel : séances générées, prix de chacune, remise, total et conflits éventuels.</summary>
    [HttpPost("quote")]
    [AllowAnonymous]
    public Task<ActionResult<TutorEngagementQuoteDto>> Quote([FromBody] TutorEngagementQuoteRequestDto request) =>
        Run(() => _service.GetQuoteAsync(request));

    /// <summary>Crée l'engagement et le paie d'avance (solde : débit atomique ; Mobile Money : paiement NotchPay initié). 409 avec la liste des conflits si une séance est indisponible.</summary>
    [HttpPost]
    public Task<ActionResult<TutorEngagementCreatedResponseDto>> Create([FromBody] CreateTutorEngagementRequestDto request) =>
        Run(() => _service.CreateAsync(User.GetUserId(), request));

    [HttpGet("{id:int}")]
    public Task<ActionResult<TutorEngagementDto>> GetById(int id) => Run(() => _service.GetByIdAsync(User.GetUserId(), id));

    /// <summary>Engagements côté payeur ou participant.</summary>
    [HttpGet("mine")]
    public Task<ActionResult<List<TutorEngagementDto>>> GetMine() => Run(() => _service.GetMineAsync(User.GetUserId()));

    /// <summary>Engagements reçus par le tuteur connecté.</summary>
    [HttpGet("tutor/mine")]
    public Task<ActionResult<List<TutorEngagementDto>>> GetTutorMine() => Run(() => _service.GetMineAsTutorAsync(User.GetUserId()));

    /// <summary>Demandes d'engagement en attente de la décision du tuteur, avec délai restant.</summary>
    [HttpGet("tutor/pending")]
    public Task<ActionResult<List<TutorPendingEngagementDto>>> GetPending() => Run(() => _service.GetPendingForTutorAsync(User.GetUserId()));

    /// <summary>Le tuteur accepte l'engagement entier.</summary>
    [HttpPut("{id:int}/accept")]
    public Task<ActionResult<TutorEngagementDto>> Accept(int id) => Run(() => _service.AcceptAsync(User.GetUserId(), id));

    /// <summary>Le tuteur refuse l'engagement entier : remboursement intégral, séance par séance.</summary>
    [HttpPut("{id:int}/decline")]
    public Task<ActionResult<TutorEngagementDto>> Decline(int id, [FromBody] DeclineTutorBookingRequestDto request) =>
        Run(() => _service.DeclineAsync(User.GetUserId(), id, request?.Reason));

    /// <summary>Annule l'engagement : séances futures annulées et remboursées (politique du tuteur par séance si le payeur annule ; intégral si le tuteur annule).</summary>
    [HttpPost("{id:int}/cancel")]
    public Task<ActionResult<TutorEngagementDto>> Cancel(int id, [FromBody] CancelTutorEngagementRequestDto request) =>
        Run(() => _service.CancelAsync(User.GetUserId(), id, request?.Reason));

    /// <summary>Active ou coupe le renouvellement automatique (paiement par solde uniquement).</summary>
    [HttpPut("{id:int}/auto-renew")]
    public Task<ActionResult<TutorEngagementDto>> SetAutoRenew(int id, [FromBody] SetTutorEngagementAutoRenewRequestDto request) =>
        Run(() => _service.SetAutoRenewAsync(User.GetUserId(), id, request.Enabled));

    /// <summary>Renouvelle l'engagement pour la période suivante, mêmes créneaux (paiement d'avance).</summary>
    [HttpPost("{id:int}/renew")]
    public Task<ActionResult<TutorEngagementCreatedResponseDto>> Renew(int id, [FromBody] RenewTutorEngagementRequestDto request) =>
        Run(() => _service.RenewAsync(User.GetUserId(), id, request));
}
