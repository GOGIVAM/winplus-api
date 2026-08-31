using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using System.Text.Json;

namespace Backend.Controllers;

public record SubscribeRequest(int PlanId, string Billing = "monthly");

[ApiController]
[Route("api/subscriptions")]
[Authorize]
public class SubscriptionsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<SubscriptionsController> _logger;

    public SubscriptionsController(ApplicationDbContext db, ILogger<SubscriptionsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>GET /api/subscriptions/me  abonnement actif de l'utilisateur connecté.</summary>
    [HttpGet("me")]
    public async Task<IActionResult> GetCurrent()
    {
        try
        {
            var userId = User.GetUserId();
            var sub = await _db.Subscriptions
                .AsNoTracking()
                .Include(s => s.PricingPlan)
                .Where(s => s.UserId == userId && !s.IsDeleted && s.Status == "active")
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            if (sub == null)
            {
                return Ok(new
                {
                    id = 0,
                    planName = "Libre",
                    tier = "free",
                    status = "active",
                    expiresAt = DateTime.UtcNow.AddYears(10),
                    autoRenew = false,
                    downloadsUsed = 0,
                    downloadsLimit = 5,
                    quizUsedToday = 0,
                    quizDailyLimit = 3,
                    aiMessagesUsed = 0,
                    aiMessagesLimit = 10,
                });
            }

            var plan = sub.PricingPlan;
            return Ok(new
            {
                id = sub.Id,
                planName = plan?.Name ?? "Standard",
                tier = plan?.Name?.ToLower() ?? "standard",
                status = sub.Status,
                expiresAt = sub.EndDate ?? sub.StartDate.AddMonths(1),
                autoRenew = sub.EndDate == null,
                downloadsUsed = 0,
                downloadsLimit = plan?.MaxDownloads ?? 30,
                quizUsedToday = 0,
                quizDailyLimit = 20,
                aiMessagesUsed = 0,
                aiMessagesLimit = plan?.MaxChatMessages ?? 100,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting current subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/subscriptions  souscrire à un plan.</summary>
    [HttpPost]
    public async Task<IActionResult> Subscribe([FromBody] SubscribeRequest req)
    {
        try
        {
            var userId = User.GetUserId();
            var plan = await _db.PricingPlans.FindAsync(req.PlanId);
            if (plan == null) return NotFound(new { error = "Plan introuvable" });

            // Annuler l'abonnement actif existant
            var existing = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.Status == "active" && !s.IsDeleted)
                .ToListAsync();
            foreach (var s in existing)
            {
                s.Status = "cancelled";
                s.EndDate = DateTime.UtcNow;
                s.UpdatedAt = DateTime.UtcNow;
            }

            var duration = req.Billing == "yearly" ? 12 : 1;
            var newSub = new Subscription
            {
                UserId = userId,
                PricingPlanId = req.PlanId,
                StartDate = DateTime.UtcNow,
                EndDate = DateTime.UtcNow.AddMonths(duration),
                Status = "active",
            };
            _db.Subscriptions.Add(newSub);
            await _db.SaveChangesAsync();

            return Ok(new { success = true, subscriptionId = newSub.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error creating subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>POST /api/subscriptions/me/cancel  résilier l'abonnement actif.</summary>
    [HttpPost("me/cancel")]
    public async Task<IActionResult> Cancel()
    {
        try
        {
            var userId = User.GetUserId();
            var sub = await _db.Subscriptions
                .Where(s => s.UserId == userId && s.Status == "active" && !s.IsDeleted)
                .OrderByDescending(s => s.StartDate)
                .FirstOrDefaultAsync();

            if (sub == null) return NotFound(new { error = "Aucun abonnement actif" });

            sub.Status = "cancelled";
            sub.EndDate = DateTime.UtcNow;
            sub.UpdatedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync();

            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling subscription");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
