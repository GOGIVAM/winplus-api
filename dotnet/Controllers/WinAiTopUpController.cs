using Backend.Extensions;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

public sealed class WinAiTopUpRequest
{
    public int Packs { get; set; } = 1;
    public string? PromoCode { get; set; }
    /// <summary>Identifiant unique de la demande (double clic, réseau rejoué) : une seule recharge.</summary>
    public string? RequestId { get; set; }
}

/// <summary>
/// Recharge de tokens WinAI par le portefeuille (lot 9.7b, 8.5 et 18.17). Le
/// prix est affiché en XAF ; la quantité en « paquets » et en multiplicateur
/// d'usage, jamais en compteur technique visible au fil de l'eau (8.3).
/// </summary>
[ApiController]
[Route("api/winai/topup")]
[Authorize]
public class WinAiTopUpController : ControllerBase
{
    private readonly ITokenTopUpService _topUp;

    public WinAiTopUpController(ITokenTopUpService topUp) => _topUp = topUp;

    [HttpGet("quote")]
    public async Task<IActionResult> Quote([FromQuery] int packs = 1, [FromQuery] string? promoCode = null) =>
        Ok(await _topUp.QuoteAsync(User.GetUserId(), packs, promoCode, HttpContext.RequestAborted));

    [HttpPost]
    public async Task<IActionResult> TopUp([FromBody] WinAiTopUpRequest req)
    {
        var result = await _topUp.TopUpAsync(User.GetUserId(), req.Packs, req.PromoCode, req.RequestId ?? Guid.NewGuid().ToString("N"), HttpContext.RequestAborted);
        return result.Succeeded
            ? Ok(new { success = true, message = result.Message, amount = result.AmountTtc })
            : StatusCode(402, new { success = false, error = result.Message });
    }

    /// <summary>Réserve rechargée restante, exprimée en paquets (arrondis) plutôt qu'en tokens.</summary>
    [HttpGet("balance")]
    public async Task<IActionResult> Balance()
    {
        var left = await _topUp.RemainingAsync(User.GetUserId(), HttpContext.RequestAborted);
        return Ok(new { hasReserve = left > 0 });
    }
}
