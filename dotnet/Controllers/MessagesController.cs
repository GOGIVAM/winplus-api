using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;

namespace Backend.Controllers;

public record StartConversationRequest(int ParticipantId, string FirstMessage);
public record SendMessageRequest(string Content);

/// <summary>
/// Messagerie directe entre utilisateurs.
/// Le concept de "conversation" est une paire (currentUserId, otherUserId).
/// L'identifiant de la conversation dans l'URL est l'ID de l'autre participant.
///
/// GET  /api/messages/conversations
/// POST /api/messages/conversations
/// GET  /api/messages/conversations/{participantId}/messages
/// POST /api/messages/conversations/{participantId}/messages
/// PUT  /api/messages/conversations/{participantId}/read
/// </summary>
[ApiController]
[Route("api/messages")]
[Authorize]
public class MessagesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<MessagesController> _logger;

    public MessagesController(ApplicationDbContext db, ILogger<MessagesController> logger)
    {
        _db = db;
        _logger = logger;
    }

    /// <summary>Liste toutes les conversations de l'utilisateur connecté.</summary>
    [HttpGet("conversations")]
    public async Task<IActionResult> GetConversations()
    {
        try
        {
            var me = User.GetUserId();

            // Récupérer les IDs des participants avec qui on a échangé
            var fromIds = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => m.ToUserId == me)
                .Select(m => m.FromUserId)
                .Distinct()
                .ToListAsync();

            var toIds = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => m.FromUserId == me)
                .Select(m => m.ToUserId)
                .Distinct()
                .ToListAsync();

            var participantIds = fromIds.Union(toIds).Distinct().ToList();

            var participants = await _db.Users
                .AsNoTracking()
                .Where(u => participantIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id);

            var conversations = new List<object>();
            foreach (var pid in participantIds)
            {
                var lastMsg = await _db.DirectMessages
                    .AsNoTracking()
                    .Where(m => (m.FromUserId == me && m.ToUserId == pid) ||
                                (m.FromUserId == pid && m.ToUserId == me))
                    .OrderByDescending(m => m.CreatedAt)
                    .FirstOrDefaultAsync();

                var unread = await _db.DirectMessages
                    .AsNoTracking()
                    .CountAsync(m => m.FromUserId == pid && m.ToUserId == me && !m.IsRead);

                if (!participants.TryGetValue(pid, out var p)) continue;

                conversations.Add(new
                {
                    id = pid,
                    participantName = $"{p.FirstName} {p.LastName}".Trim(),
                    participantRole = p.Role,
                    avatarUrl = p.AvatarUrl,
                    lastMessage = lastMsg?.Content,
                    lastMessageAt = lastMsg?.CreatedAt,
                    unreadCount = unread,
                });
            }

            conversations = conversations
                .OrderByDescending(c => (DateTime?)((dynamic)c).lastMessageAt ?? DateTime.MinValue)
                .Cast<object>()
                .ToList();

            return Ok(conversations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting conversations");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Démarrer une nouvelle conversation (envoie le premier message).</summary>
    [HttpPost("conversations")]
    public async Task<IActionResult> StartConversation([FromBody] StartConversationRequest req)
    {
        try
        {
            var me = User.GetUserId();
            if (me == req.ParticipantId)
                return BadRequest(new { error = "Impossible de se contacter soi-même" });

            var other = await _db.Users.FindAsync(req.ParticipantId);
            if (other == null) return NotFound(new { error = "Utilisateur introuvable" });

            var msg = new DirectMessage
            {
                FromUserId = me,
                ToUserId = req.ParticipantId,
                Content = req.FirstMessage,
            };
            _db.DirectMessages.Add(msg);
            await _db.SaveChangesAsync();

            return Ok(new { conversationId = req.ParticipantId, messageId = msg.Id });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error starting conversation");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Récupère les messages d'une conversation.</summary>
    [HttpGet("conversations/{participantId:int}/messages")]
    public async Task<IActionResult> GetMessages([FromRoute] int participantId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        try
        {
            var me = User.GetUserId();
            if (pageSize > 100) pageSize = 100;

            var messages = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => (m.FromUserId == me && m.ToUserId == participantId) ||
                            (m.FromUserId == participantId && m.ToUserId == me))
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(m => new
                {
                    id = m.Id,
                    content = m.Content,
                    isFromMe = m.FromUserId == me,
                    sentAt = m.CreatedAt,
                    isRead = m.IsRead,
                })
                .ToListAsync();

            return Ok(messages.OrderBy(m => m.sentAt));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting messages");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Envoie un message dans une conversation.</summary>
    [HttpPost("conversations/{participantId:int}/messages")]
    public async Task<IActionResult> SendMessage([FromRoute] int participantId,
        [FromBody] SendMessageRequest req)
    {
        try
        {
            var me = User.GetUserId();
            var other = await _db.Users.FindAsync(participantId);
            if (other == null) return NotFound(new { error = "Utilisateur introuvable" });

            var msg = new DirectMessage
            {
                FromUserId = me,
                ToUserId = participantId,
                Content = req.Content,
            };
            _db.DirectMessages.Add(msg);
            await _db.SaveChangesAsync();

            return Ok(new { id = msg.Id, sentAt = msg.CreatedAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Marque tous les messages de la conversation comme lus.</summary>
    [HttpPut("conversations/{participantId:int}/read")]
    public async Task<IActionResult> MarkRead([FromRoute] int participantId)
    {
        try
        {
            var me = User.GetUserId();
            var unread = await _db.DirectMessages
                .Where(m => m.FromUserId == participantId && m.ToUserId == me && !m.IsRead)
                .ToListAsync();

            foreach (var m in unread)
            {
                m.IsRead = true;
                m.ReadAt = DateTime.UtcNow;
            }
            await _db.SaveChangesAsync();

            return Ok(new { markedRead = unread.Count });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking messages as read");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }
}
