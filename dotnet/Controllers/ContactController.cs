using System.ComponentModel.DataAnnotations;
using System.Net;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Backend.Controllers;

/// <summary>
/// Formulaire de contact (25/11/2026). Avant, la page Contact (POST /contact) et le centre d'aide
/// (POST /support/contact) visaient des routes inexistantes : 404, et le centre d'aide affichait « envoyé »
/// quand même. Les messages de visiteurs étaient perdus.
///   POST /api/support/contact        ouvert sans compte (rattaché au compte s'il est connecté), limité à
///                                    5 envois par heure et par adresse IP (RateLimitingMiddleware) ;
///                                    message gardé en base PUIS envoyé par e-mail à l'équipe.
///   GET  /api/admin/contact-messages les messages reçus (administration), pour qu'aucun ne se perde même si
///                                    l'e-mail n'est pas parti.
/// </summary>
[ApiController]
public class ContactController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IEmailService _email;
    private readonly IConfiguration _config;
    private readonly ILogger<ContactController> _logger;

    public ContactController(ApplicationDbContext db, IEmailService email, IConfiguration config, ILogger<ContactController> logger)
    {
        _db = db; _email = email; _config = config; _logger = logger;
    }

    public sealed class ContactRequest
    {
        [Required, StringLength(100, MinimumLength = 2)]
        public string Name { get; set; } = string.Empty;
        [Required, EmailAddress, StringLength(254)]
        public string Email { get; set; } = string.Empty;
        [StringLength(30)]
        public string? Phone { get; set; }
        [Required, StringLength(150, MinimumLength = 3)]
        public string Subject { get; set; } = string.Empty;
        [Required, StringLength(5000, MinimumLength = 10)]
        public string Message { get; set; } = string.Empty;
        /// <summary>contact (page Contact), aide (centre d'aide), mobile.</summary>
        [StringLength(20)]
        public string? Source { get; set; }
        [StringLength(10)]
        public string? Locale { get; set; }
        /// <summary>Champ piège invisible pour les robots : rempli, le message est ignoré (réponse normale).</summary>
        public string? Website { get; set; }
    }

    [HttpPost("api/support/contact")]
    [AllowAnonymous]
    public async Task<IActionResult> Envoyer([FromBody] ContactRequest req, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { success = false, code = "invalid", error = "Vérifiez les champs du formulaire.",
                fields = ModelState.Where(m => m.Value?.Errors.Count > 0).Select(m => m.Key) });
        if (!string.IsNullOrWhiteSpace(req.Website))
            return StatusCode(201, new { success = true }); // robot : rien n'est gardé ni envoyé

        int? userId = null;
        if (User.Identity?.IsAuthenticated == true) { try { userId = User.GetUserId(); } catch { /* jeton sans identifiant */ } }

        var source = req.Source?.Trim().ToLowerInvariant() is "aide" or "mobile" or "contact" ? req.Source.Trim().ToLowerInvariant() : "contact";
        var message = new ContactMessage
        {
            UserId = userId, Name = req.Name.Trim(), Email = req.Email.Trim(), Phone = string.IsNullOrWhiteSpace(req.Phone) ? null : req.Phone.Trim(),
            Subject = req.Subject.Trim(), Message = req.Message.Trim(), Source = source,
            Locale = string.IsNullOrWhiteSpace(req.Locale) ? null : req.Locale.Trim(),
        };
        _db.ContactMessages.Add(message);
        await _db.SaveChangesAsync(ct);

        // E-mail à l'équipe : le message est déjà gardé, un échec d'envoi ne le perd pas.
        try
        {
            var destinataire = _config["Support:InboxEmail"] ?? "support@winplus.cm";
            string H(string? s) => WebUtility.HtmlEncode(s ?? "");
            var html = $@"<p><strong>Nouveau message ({H(source)}) n° {message.Id}</strong></p>
<p><strong>De :</strong> {H(message.Name)} &lt;{H(message.Email)}&gt;{(message.Phone != null ? " · " + H(message.Phone) : "")}{(userId != null ? $" · compte n° {userId}" : " · sans compte")}</p>
<p><strong>Sujet :</strong> {H(message.Subject)}</p>
<div style=""white-space:pre-wrap;border-left:3px solid #33BBAF;padding-left:12px"">{H(message.Message)}</div>
<p style=""color:#777"">Répondre directement à {H(message.Email)}.</p>";
            message.EmailSent = await _email.SendGenericEmailAsync(destinataire, $"[Contact WinPlus] {message.Subject}", html, NotificationCategory.Transactional);
            await _db.SaveChangesAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Contact : message {Id} gardé mais e-mail non envoyé", message.Id);
        }

        return StatusCode(201, new { success = true, id = message.Id });
    }

    [HttpGet("api/admin/contact-messages")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> Liste([FromQuery] string? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = _db.ContactMessages.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status)) q = q.Where(m => m.Status == status);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.CreatedAt).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);
        return Ok(new { success = true, data = new { total, page, pageSize, items } });
    }

    [HttpPost("api/admin/contact-messages/{id:int}/handled")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> MarquerTraite(int id, CancellationToken ct)
    {
        var m = await _db.ContactMessages.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (m == null) return NotFound(new { success = false });
        m.Status = "handled"; m.HandledAt = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);
        return Ok(new { success = true });
    }
}
