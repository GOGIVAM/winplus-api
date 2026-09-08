using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http.Json;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Services;

namespace Backend.Controllers;

public record StartConversationRequest(int ParticipantId, string FirstMessage);
public record SendMessageRequest(string? Content, string? Type, string? FileUrl, string? FileName, int? ReplyToMessageId, DateTime? ScheduledSendAt);
public record ReactToMessageRequest(string Emoji);
public record QuickRepliesRequest(string MessageText);
public record GenerateReplyRequest(string MessageText, string? Context);
public record ParentReportRequest(string StudentName, string SummaryText);
public record SaveTemplateRequest(string Name, string Text);

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
    private readonly IStorageService _storage;

    private static readonly Dictionary<string, string[]> AllowedAttachmentExtensions = new()
    {
        ["image"] = new[] { ".jpg", ".jpeg", ".png", ".gif", ".webp" },
        ["pdf"] = new[] { ".pdf" },
        ["voice"] = new[] { ".webm", ".m4a", ".mp3", ".ogg", ".wav" },
    };
    private const long MaxAttachmentSize = 20 * 1024 * 1024; // 20 Mo

    public MessagesController(ApplicationDbContext db, ILogger<MessagesController> logger, IHttpClientFactory http, IStorageService storage)
    {
        _db = db;
        _logger = logger;
        _http = http;
        _storage = storage;
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

        // 6. Professeur ↔ élève inscrit à une de ses formations (Module 9,
        // US-FOR-07 : la relance d'inactivité crée un DirectMessage — sans ce
        // lien, l'élève recevait le message mais ne pouvait pas y répondre).
        if (await _db.CourseEnrollments.AnyAsync(e => e.IsActive &&
            ((e.Course.InstructorId == userId1 && e.UserId == userId2) ||
             (e.Course.InstructorId == userId2 && e.UserId == userId1))))
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
            var now = DateTime.UtcNow;

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
                    .Where(m => !m.IsDeleted && (m.ScheduledSendAt == null || m.ScheduledSendAt <= now || m.FromUserId == me))
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
                    lastMessage = lastMsg?.Content ?? (lastMsg != null ? (lastMsg.Type == "text" ? null : "📎 Pièce jointe") : null),
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

    /// <summary>Upload d'une pièce jointe (image, PDF, note vocale) pour la messagerie (US-MSG-02).</summary>
    [HttpPost("attachments")]
    [RequestSizeLimit(MaxAttachmentSize)]
    public async Task<IActionResult> UploadAttachment([FromForm] IFormFile file, [FromForm] string type)
    {
        try
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { error = "Aucun fichier fourni." });
            if (file.Length > MaxAttachmentSize)
                return BadRequest(new { error = "Fichier trop volumineux (max 20 Mo)." });
            if (!AllowedAttachmentExtensions.TryGetValue(type, out var allowedExt))
                return BadRequest(new { error = "Type de pièce jointe invalide (image, pdf ou voice)." });

            var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (!allowedExt.Contains(ext))
                return BadRequest(new { error = $"Extension non autorisée pour ce type ({string.Join(", ", allowedExt)})." });

            var me = User.GetUserId();
            var key = $"chat-attachments/{me}/{Guid.NewGuid()}{ext}";
            using var stream = file.OpenReadStream();
            var url = await _storage.PutAsync(stream, key, file.ContentType);

            return Ok(new { url, fileName = file.FileName, type });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error uploading message attachment");
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
            var now = DateTime.UtcNow;

            var page1 = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => (m.FromUserId == me && m.ToUserId == participantId) ||
                            (m.FromUserId == participantId && m.ToUserId == me))
                // Un message programmé reste invisible du destinataire tant que l'heure n'est pas atteinte.
                .Where(m => m.ScheduledSendAt == null || m.ScheduledSendAt <= now || m.FromUserId == me)
                .OrderByDescending(m => m.CreatedAt)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            var ids = page1.Select(m => m.Id).ToList();
            var replyIds = page1.Where(m => m.ReplyToMessageId.HasValue).Select(m => m.ReplyToMessageId!.Value).Distinct().ToList();

            var reactions = await _db.DirectMessageReactions
                .AsNoTracking()
                .Where(r => ids.Contains(r.DirectMessageId))
                .ToListAsync();

            var replyPreviews = await _db.DirectMessages
                .AsNoTracking()
                .Where(m => replyIds.Contains(m.Id))
                .ToDictionaryAsync(m => m.Id);

            var messages = page1.Select(m => new
            {
                id = m.Id,
                content = m.IsDeleted ? null : m.Content,
                type = m.Type,
                fileUrl = m.IsDeleted ? null : m.FileUrl,
                fileName = m.FileName,
                isDeleted = m.IsDeleted,
                isFromMe = m.FromUserId == me,
                sentAt = m.CreatedAt,
                isRead = m.IsRead,
                readAt = m.ReadAt,
                scheduledSendAt = m.ScheduledSendAt,
                isPendingScheduled = m.ScheduledSendAt.HasValue && m.ScheduledSendAt > now,
                replyTo = m.ReplyToMessageId.HasValue && replyPreviews.TryGetValue(m.ReplyToMessageId.Value, out var rt)
                    ? new { id = rt.Id, content = rt.IsDeleted ? "Message supprimé" : (rt.Content ?? rt.FileName ?? "Pièce jointe"), isFromMe = rt.FromUserId == me }
                    : null,
                reactions = reactions.Where(r => r.DirectMessageId == m.Id)
                    .Select(r => new { userId = r.UserId, emoji = r.Emoji, isMine = r.UserId == me })
                    .ToList(),
            }).ToList();

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

            var type = string.IsNullOrWhiteSpace(req.Type) ? "text" : req.Type;
            if (string.IsNullOrWhiteSpace(req.Content) && string.IsNullOrWhiteSpace(req.FileUrl))
                return BadRequest(new { error = "Message vide : ajoute du texte ou une pièce jointe." });

            if (!await AreLinkedAsync(me, participantId))
                return StatusCode(403, new { error = "Vous ne pouvez envoyer un message qu'à vos contacts liés." });

            if (!string.IsNullOrWhiteSpace(req.Content) && await IsMessageBlockedAsync(req.Content, me))
                return UnprocessableEntity(new { error = "Votre message contient un contenu inapproprié et n'a pas pu être envoyé." });

            if (req.ReplyToMessageId.HasValue)
            {
                var replyExists = await _db.DirectMessages.AnyAsync(m => m.Id == req.ReplyToMessageId.Value &&
                    ((m.FromUserId == me && m.ToUserId == participantId) || (m.FromUserId == participantId && m.ToUserId == me)));
                if (!replyExists) return BadRequest(new { error = "Message cité introuvable dans cette conversation." });
            }

            var isScheduled = req.ScheduledSendAt.HasValue && req.ScheduledSendAt.Value > DateTime.UtcNow;

            var msg = new DirectMessage
            {
                FromUserId = me,
                ToUserId = participantId,
                Content = req.Content,
                Type = type,
                FileUrl = req.FileUrl,
                FileName = req.FileName,
                ReplyToMessageId = req.ReplyToMessageId,
                ScheduledSendAt = isScheduled ? req.ScheduledSendAt : null,
                ScheduledNotificationSent = !isScheduled,
            };
            _db.DirectMessages.Add(msg);
            await _db.SaveChangesAsync();

            return Ok(new { id = msg.Id, sentAt = msg.CreatedAt, scheduledSendAt = msg.ScheduledSendAt });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending message");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Annule un message programmé avant son heure d'envoi (US-MSG-06).</summary>
    [HttpDelete("{id:int}/scheduled")]
    public async Task<IActionResult> CancelScheduled(int id)
    {
        try
        {
            var me = User.GetUserId();
            var msg = await _db.DirectMessages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound(new { error = "Message introuvable." });
            if (msg.FromUserId != me) return StatusCode(403, new { error = "Tu ne peux annuler que tes propres messages programmés." });
            if (!msg.ScheduledSendAt.HasValue || msg.ScheduledSendAt.Value <= DateTime.UtcNow)
                return BadRequest(new { error = "Ce message n'est plus programmé — il a déjà été envoyé." });

            _db.DirectMessages.Remove(msg);
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error cancelling scheduled message");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Liste mes messages programmés pas encore envoyés (section "À envoyer", US-MSG-06).</summary>
    [HttpGet("scheduled")]
    public async Task<IActionResult> GetScheduled()
    {
        var me = User.GetUserId();
        var now = DateTime.UtcNow;
        var scheduled = await _db.DirectMessages
            .AsNoTracking()
            .Include(m => m.To)
            .Where(m => m.FromUserId == me && m.ScheduledSendAt != null && m.ScheduledSendAt > now)
            .OrderBy(m => m.ScheduledSendAt)
            .Select(m => new
            {
                id = m.Id,
                participantId = m.ToUserId,
                participantName = m.To != null ? $"{m.To.FirstName} {m.To.LastName}".Trim() : null,
                content = m.Content,
                type = m.Type,
                scheduledSendAt = m.ScheduledSendAt,
            })
            .ToListAsync();
        return Ok(scheduled);
    }

    /// <summary>
    /// Recherche textuelle dans toutes mes conversations, avec filtres (US-MSG-09).
    /// Retourne le message trouvé et l'identité de la conversation ; le contexte
    /// (5 messages avant/après) est affiché en ouvrant la conversation ciblée
    /// côté front plutôt que renvoyé ici — évite un aller-retour N+1 par résultat.
    /// </summary>
    [HttpGet("search")]
    public async Task<IActionResult> Search(
        [FromQuery] string? q, [FromQuery] bool unreadOnly = false, [FromQuery] bool hasAttachment = false,
        [FromQuery] int? conversationId = null, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
    {
        var me = User.GetUserId();
        var now = DateTime.UtcNow;

        var query = _db.DirectMessages.AsNoTracking()
            .Where(m => (m.FromUserId == me || m.ToUserId == me) && !m.IsDeleted)
            .Where(m => m.ScheduledSendAt == null || m.ScheduledSendAt <= now || m.FromUserId == me);

        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(m => m.Content != null && EF.Functions.ILike(m.Content, $"%{q}%"));
        if (unreadOnly)
            query = query.Where(m => m.ToUserId == me && !m.IsRead);
        if (hasAttachment)
            query = query.Where(m => m.Type != "text");
        if (conversationId.HasValue)
            query = query.Where(m => m.FromUserId == conversationId.Value || m.ToUserId == conversationId.Value);
        if (from.HasValue)
            query = query.Where(m => m.CreatedAt >= from.Value);
        if (to.HasValue)
            query = query.Where(m => m.CreatedAt <= to.Value);

        var matches = await query.OrderByDescending(m => m.CreatedAt).Take(50).ToListAsync();

        var participantIds = matches.Select(m => m.FromUserId == me ? m.ToUserId : m.FromUserId).Distinct().ToList();
        var participants = await _db.Users.AsNoTracking()
            .Where(u => participantIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.FirstName} {u.LastName}".Trim());

        var results = matches.Select(m =>
        {
            var otherId = m.FromUserId == me ? m.ToUserId : m.FromUserId;
            return new
            {
                messageId = m.Id,
                conversationId = otherId,
                participantName = participants.GetValueOrDefault(otherId, "Utilisateur"),
                content = m.Content,
                type = m.Type,
                fileName = m.FileName,
                fileUrl = m.FileUrl,
                sentAt = m.CreatedAt,
                isFromMe = m.FromUserId == me,
            };
        });

        return Ok(results);
    }

    /// <summary>Supprime (soft-delete) un de mes messages.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteMessage(int id)
    {
        try
        {
            var me = User.GetUserId();
            var msg = await _db.DirectMessages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound(new { error = "Message introuvable." });
            if (msg.FromUserId != me) return StatusCode(403, new { error = "Tu ne peux supprimer que tes propres messages." });

            msg.IsDeleted = true;
            await _db.SaveChangesAsync();
            return Ok(new { success = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error deleting message");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    /// <summary>Ajoute ou retire (toggle) ma réaction emoji sur un message.</summary>
    [HttpPost("{id:int}/react")]
    public async Task<IActionResult> React(int id, [FromBody] ReactToMessageRequest req)
    {
        try
        {
            var me = User.GetUserId();
            var msg = await _db.DirectMessages.FirstOrDefaultAsync(m => m.Id == id);
            if (msg == null) return NotFound(new { error = "Message introuvable." });
            if (msg.FromUserId != me && msg.ToUserId != me)
                return StatusCode(403, new { error = "Tu n'as pas accès à ce message." });

            var existing = await _db.DirectMessageReactions
                .FirstOrDefaultAsync(r => r.DirectMessageId == id && r.UserId == me);

            if (existing != null && existing.Emoji == req.Emoji)
            {
                _db.DirectMessageReactions.Remove(existing);
            }
            else if (existing != null)
            {
                existing.Emoji = req.Emoji;
            }
            else
            {
                _db.DirectMessageReactions.Add(new DirectMessageReaction { DirectMessageId = id, UserId = me, Emoji = req.Emoji });
            }
            await _db.SaveChangesAsync();

            var current = await _db.DirectMessageReactions
                .Where(r => r.DirectMessageId == id)
                .Select(r => new { userId = r.UserId, emoji = r.Emoji, isMine = r.UserId == me })
                .ToListAsync();
            return Ok(current);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error reacting to message");
            return StatusCode(500, new { error = "Internal server error" });
        }
    }

    private async Task<IActionResult> ProxyToPythonAsync(string path, object payload)
    {
        var client = _http.CreateClient("FastApiClient");
        var token = HttpContext.Request.Headers.Authorization.FirstOrDefault();
        if (!string.IsNullOrEmpty(token) && token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            client.DefaultRequestHeaders.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token["Bearer ".Length..]);

        var res = await client.PostAsJsonAsync(path, payload);
        var body = await res.Content.ReadAsStringAsync();
        Response.StatusCode = (int)res.StatusCode;
        return Content(body, "application/json");
    }

    /// <summary>WinAI propose 3 réponses courtes contextuelles à un message reçu (US-MSG-07).</summary>
    [HttpPost("quick-replies")]
    public Task<IActionResult> QuickReplies([FromBody] QuickRepliesRequest req)
        => ProxyToPythonAsync("/api/messaging/quick-replies", new { message_text = req.MessageText });

    /// <summary>WinAI génère une réponse longue et rédigée pour un message complexe (US-MSG-07).</summary>
    [HttpPost("generate-reply")]
    public Task<IActionResult> GenerateReply([FromBody] GenerateReplyRequest req)
        => ProxyToPythonAsync("/api/messaging/generate-reply", new { message_text = req.MessageText, context = req.Context });

    /// <summary>WinAI formalise un résumé libre en communication destinée aux parents (US-MSG-10).</summary>
    [HttpPost("parent-report")]
    public Task<IActionResult> ParentReport([FromBody] ParentReportRequest req)
        => ProxyToPythonAsync("/api/messaging/parent-report", new { student_name = req.StudentName, summary_text = req.SummaryText });

    /// <summary>Mes modèles de message enregistrés (US-MSG-08).</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates()
    {
        var me = User.GetUserId();
        var templates = await _db.MessageTemplates.AsNoTracking()
            .Where(t => t.UserId == me).OrderByDescending(t => t.CreatedAt).ToListAsync();
        return Ok(templates);
    }

    /// <summary>Enregistre un nouveau modèle (max 20 par utilisateur).</summary>
    [HttpPost("templates")]
    public async Task<IActionResult> CreateTemplate([FromBody] SaveTemplateRequest req)
    {
        var me = User.GetUserId();
        var count = await _db.MessageTemplates.CountAsync(t => t.UserId == me);
        if (count >= 20)
            return BadRequest(new { error = "Limite de 20 modèles atteinte. Supprime-en un pour en ajouter un nouveau." });
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Text))
            return BadRequest(new { error = "Nom et texte requis." });

        var template = new MessageTemplate { UserId = me, Name = req.Name.Trim(), Text = req.Text.Trim() };
        _db.MessageTemplates.Add(template);
        await _db.SaveChangesAsync();
        return Ok(template);
    }

    /// <summary>Modifie un modèle existant.</summary>
    [HttpPut("templates/{id:int}")]
    public async Task<IActionResult> UpdateTemplate(int id, [FromBody] SaveTemplateRequest req)
    {
        var me = User.GetUserId();
        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == me);
        if (template == null) return NotFound();
        template.Name = req.Name.Trim();
        template.Text = req.Text.Trim();
        await _db.SaveChangesAsync();
        return Ok(template);
    }

    /// <summary>Supprime un modèle.</summary>
    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        var me = User.GetUserId();
        var template = await _db.MessageTemplates.FirstOrDefaultAsync(t => t.Id == id && t.UserId == me);
        if (template == null) return NotFound();
        _db.MessageTemplates.Remove(template);
        await _db.SaveChangesAsync();
        return Ok(new { success = true });
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
