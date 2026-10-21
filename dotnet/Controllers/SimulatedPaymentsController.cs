using Backend.Data;
using Backend.Extensions;
using Backend.Models.DTOs;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Panneau de test des paiements simulés (integration.winplus.cm seulement).
/// Hors intégration, chaque route répond 404 comme si elle n'existait pas
/// (<see cref="PaymentSimulationGuard"/>, relu à chaque requête).
/// Un utilisateur ne voit et ne décide que ses propres paiements ; un
/// administrateur voit tous les paiements en attente.
/// </summary>
[ApiController]
[Route("api/payments/simulated")]
[Authorize]
[Produces("application/json")]
public class SimulatedPaymentsController : ControllerBase
{
    private readonly IConfiguration _configuration;
    private readonly IServiceProvider _services;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SimulatedPaymentsController> _logger;

    public SimulatedPaymentsController(IConfiguration configuration, IServiceProvider services, ApplicationDbContext db, ILogger<SimulatedPaymentsController> logger)
    {
        _configuration = configuration;
        _services = services;
        _db = db;
        _logger = logger;
    }

    private SimulatedPaymentStore? Store =>
        PaymentSimulationGuard.IsEnabled(_configuration) ? _services.GetService<SimulatedPaymentStore>() : null;

    private async Task<string?> CurrentEmailAsync()
    {
        var userId = User.GetUserId();
        return await _db.Users.AsNoTracking().Where(u => u.Id == userId).Select(u => u.Email).FirstOrDefaultAsync();
    }

    /// <summary>GET /api/payments/simulated : paiements simulés en attente.</summary>
    [HttpGet]
    public async Task<IActionResult> Pending()
    {
        var store = Store;
        if (store == null) return NotFound();
        var email = User.IsInRole("admin") ? null : await CurrentEmailAsync() ?? string.Empty;
        return Ok(new
        {
            simulated = true,
            payments = store.Pending(email).Select(p => new
            {
                reference = p.Reference,
                amount = p.Amount,
                description = p.Description,
                channel = p.Channel,
                customerEmail = p.CustomerEmail,
                createdAt = p.CreatedAt,
            }),
        });
    }

    /// <summary>POST /api/payments/simulated/{reference}/confirm | /refuse.</summary>
    [HttpPost("{reference}/{decision:regex(^(confirm|refuse)$)}")]
    public async Task<IActionResult> Decide(string reference, string decision)
    {
        var store = Store;
        if (store == null) return NotFound();
        var payment = store.Get(reference);
        if (payment == null) return NotFound(new { error = "Paiement simulé introuvable (le service a peut-être redémarré)." });
        if (!User.IsInRole("admin")
            && !string.Equals(payment.CustomerEmail, await CurrentEmailAsync(), StringComparison.OrdinalIgnoreCase))
            return StatusCode(403, new { error = "Ce paiement n'est pas le vôtre." });

        var success = decision == "confirm";
        if (!store.Decide(reference, success))
            return Conflict(new { error = "Ce paiement a déjà été confirmé ou refusé." });

        var status = success ? "complete" : "failed";
        var transaction = new NotchPayWebhookTransaction
        {
            Reference = reference,
            Status = status,
            Amount = payment.Amount,
            Currency = "XAF",
            Operator = "simulation",
            FailureCode = success ? null : "simulated_refusal",
            FailureMessage = success ? null : "Paiement refusé depuis le panneau de test",
        };
        await _services.GetRequiredService<INotchPayEventDispatcher>()
            .DispatchAsync($"sim:{reference}:{status}", $"payment.{status}", transaction);
        _logger.LogWarning("PAIEMENT SIMULÉ {Ref} : {Status} par l'utilisateur {UserId}", reference, status, User.GetUserId());
        return Ok(new { reference, status });
    }
}
