using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
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
    private readonly IHttpClientFactory _http;

    public MessagesController(ApplicationDbContext db, ILogger<MessagesController> logger, IHttpClientFactory http)
    {
        _db = db;
        _logger = logger;
        _http = http;
    }

    private sealed record MessageModerationResult(string? Verdict, double Confidence, string? Reason, string? Action);

    private async Task<bool> IsMessageBlockedAsync(string content, int authorId)
    {
        try
        {
            var token = HttpContext.Request.Headers.Authorization.FirstOrDefault();
            var client = _http.CreateClient("FastApiClient");
            if (!string.IsNullOrEmpty(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token["Bearer ".Length..]);

            var resp = await client.PostAsJsonAsync("/api/admin/moderate-message", new
            {
                content = content[..Math.Min(content.Length, 800)],
                author_id = authorId,
                confidence_threshold = 0.8,
            });

            if (!resp.IsSuccessStatusCode) return false;
            var result = await resp.Content.ReadFromJsonAsync<MessageModerationResult>();
            return result?.Action == "hold_for_review";
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Message moderation check failed  message autorisé par défaut");
            return false;
        }
    }

    private async Task<bool> AreLinkedAsync(int userId1, int userId2)
    {
        // 1. Parent-élève (dans les deux sens)
        if (await _db.ParentStudentLinks.AnyAsync(l =>
            (l.ParentId == userId1 && l.StudentId == userId2) ||
            (l.ParentId == userId2 && l.StudentId == userId1)))
            return true;

        // 2. Même classe via TeacherClassStudent (prof-élève)
        var u1ClassIds = await _db.TeacherClassStudents
            .Where(tcs => tcs.StudentId == userId1)
            .Select(tcs => tcs.TeacherClassId)
            .Union(_db.TeacherClasses.Where(tc => tc.TeacherId == userId1).Select(tc => tc.Id))
            .ToListAsync();

        var u2ClassIds = await _db.TeacherClassStudents
            .Where(tcs => tcs.StudentId == userId2)
            .Select(tcs => tcs.TeacherClassId)
            .Union(_db.TeacherClasses.Where(tc => tc.TeacherId == userId2).Select(tc => tc.Id))
            .ToListAsync();

        if (u1ClassIds.Intersect(u2ClassIds).Any()) return true;

        // 3. Liaison directe prof-élève acceptée
        if (await _db.TeacherStudentLinks.AnyAsync(l =>
            l.Status == "accepted" &&
            ((l.TeacherId == userId1 && l.StudentId == userId2) ||
             (l.TeacherId == userId2 && l.StudentId == userId1))))
            return true;

        // 4. Même groupe d'étude
        var u1Groups = await _db.StudyGroupMembers
            .Where(m => m.UserId == userId1).Select(m => m.StudyGroupId).ToListAsync();
        var u2Groups = await _db.StudyGroupMembers
            .Where(m => m.UserId == userId2).Select(m => m.StudyGroupId).ToListAsync();
        if (u1Groups.Intersect(u2Groups).Any()) return true;

        // 5. Institution → élève (via InstitutionId sur User, ou InstitutionStudent)
        if (await _db.InstitutionStudents.AnyAsync(s =>
            (s.InstitutionId == userId1 && s.StudentId == userId2) ||
            (s.InstitutionId == userId2 && s.StudentId == userId1)))
            return true;

        return false;
    }

    /// <summary>Retourne la liste des contacts avec qui l'utilisateur peut échanger.</summary>
    [HttpGet("contacts")]
    public async Task<IActionResult> GetContacts()
    {
        try
        {
            var me = User.GetUserId();
            var contactIds = new HashSet<int>();

            // 1. Enfants (si parent) / parents (si élève)
            var childIds = await _db.ParentStudentLinks
                .Where(l => l.ParentId == me).Select(l => l.StudentId).ToListAsync();
            var parentIds = await _db.ParentStudentLinks
                .Where(l => l.StudentId == me).Select(l => l.ParentId).ToListAsync();
            contactIds.UnionWith(childIds);
            contactIds.UnionWith(parentIds);

            // 2. Même classe (prof ou élève)
            var myClassIds = await _db.TeacherClasses
                .Where(tc => tc.TeacherId == me).Select(tc => tc.Id).ToListAsync();
            var studentIdsInMyClasses = await _db.TeacherClassStudents
                .Where(tcs => myClassIds.Contains(tcs.TeacherClassId))
                .Select(tcs => tcs.StudentId).ToListAsync();
            contactIds.UnionWith(studentIdsInMyClasses);

            var classIdsAsStudent = await _db.TeacherClassStudents
                .Where(tcs => tcs.StudentId == me).Select(tcs => tcs.TeacherClassId).ToListAsync();
            var teacherIdsFromClasses = await _db.TeacherClasses
                .Where(tc => classIdsAsStudent.Contains(tc.Id)).Select(tc => tc.TeacherId).ToListAsync();
            contactIds.UnionWith(teacherIdsFromClasses);

            // 3. Liaisons directes prof-élève acceptées
            var directLinks = await _db.TeacherStudentLinks
                .Where(l => l.Status == "accepted" && (l.TeacherId == me || l.StudentId == me))
                .Select(l => l.TeacherId == me ? l.StudentId : l.TeacherId)
                .ToListAsync();
            contactIds.UnionWith(directLinks);

            // 4. Mêmes groupes d'étude
            var myGroupIds = await _db.StudyGroupMembers
                .Where(m => m.UserId == me).Select(m => m.StudyGroupId).ToListAsync();
            var groupMemberIds = await _db.StudyGroupMembers
                .Where(m => myGroupIds.Contains(m.StudyGroupId) && m.UserId != me)
                .Select(m => m.UserId).ToListAsync();
            contactIds.UnionWith(groupMemberIds);

            // 5. Institution (via InstitutionStudent.InstitutionId)
            var instStudents = await _db.InstitutionStudents
                .Where(s => s.InstitutionId == me).Select(s => s.StudentId).ToListAsync();
            var instForStudent = await _db.InstitutionStudents
                .Where(s => s.StudentId == me).Select(s => s.InstitutionId).ToListAsync();
            contactIds.UnionWith(instStudents);
            contactIds.UnionWith(instForStudent);

            contactIds.Remove(me);

            var contacts = await _db.Users
                .AsNoTracking()
                .Where(u => contactIds.Contains(u.Id))
                .Select(u => new
                {
                    u.Id,
                    u.FirstName,
                    u.LastName,
                    u.Role,
                    u.AvatarUrl,
                    IsVerified = u.Role == "institution" && u.IsEmailVerified,
                })
                .ToListAsync();

            return Ok(contacts);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting contacts");
            return StatusCode(500, new { error = "Internal server error" });
        }
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

            if (!await AreLinkedAsync(me, req.ParticipantId))
                return StatusCode(403, new { error = "Vous ne pouvez envoyer un message qu'à vos contacts liés." });

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

            if (!await AreLinkedAsync(me, participantId))
                return StatusCode(403, new { error = "Vous ne pouvez envoyer un message qu'à vos contacts liés." });

            if (await IsMessageBlockedAsync(req.Content, me))
                return UnprocessableEntity(new { error = "Votre message contient un contenu inapproprié et n'a pas pu être envoyé." });

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
