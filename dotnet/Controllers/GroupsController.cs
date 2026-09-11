using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Backend.Data;
using Backend.Extensions;
using Backend.Models.Entities;
using Backend.Utils;

namespace Backend.Controllers;

public record CreateChatGroupRequest(string Name, string? PhotoUrl, List<int>? MemberIds, int? FromClassId);
public record SendGroupMessageRequest(string? Content, string? Type, string? FileUrl, string? FileName);
public record SetAnnouncementModeRequest(bool Enabled);

/// <summary>
/// Groupes de messagerie (US-MSG-03, Module 7) : classes, groupes de révision,
/// collègues. Distinct de MessagesController (1-1) et de CourseChannelController
/// (canal public par formation).
/// </summary>
[ApiController]
[Route("api/groups")]
[Authorize]
public class GroupsController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly ILogger<GroupsController> _logger;

    public GroupsController(ApplicationDbContext db, ILogger<GroupsController> logger)
    {
        _db = db;
        _logger = logger;
    }

    private async Task<bool> IsMemberAsync(int groupId, int userId) =>
        await _db.ChatGroupMembers.AnyAsync(m => m.ChatGroupId == groupId && m.UserId == userId);

    private async Task<bool> IsAdminAsync(int groupId, int userId) =>
        await _db.ChatGroupMembers.AnyAsync(m => m.ChatGroupId == groupId && m.UserId == userId && m.Role == "admin");

    /// <summary>Crée un groupe manuellement ou depuis une classe existante (import automatique des membres).</summary>
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateChatGroupRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name))
            return BadRequest(new { error = "Le nom du groupe est requis." });

        var me = User.GetUserId();
        var group = new ChatGroup { Name = req.Name.Trim(), PhotoUrl = req.PhotoUrl, CreatorId = me };
        _db.ChatGroups.Add(group);
        await _db.SaveChangesAsync();

        var memberIds = new HashSet<int>(req.MemberIds ?? new List<int>());
        if (req.FromClassId.HasValue)
        {
            var classMembers = await _db.TeacherClassStudents
                .Where(tcs => tcs.TeacherClassId == req.FromClassId.Value)
                .Select(tcs => tcs.StudentId)
                .ToListAsync();
            memberIds.UnionWith(classMembers);
        }
        memberIds.Remove(me);

        _db.ChatGroupMembers.Add(new ChatGroupMember { ChatGroupId = group.Id, UserId = me, Role = "admin" });
        foreach (var uid in memberIds)
            _db.ChatGroupMembers.Add(new ChatGroupMember { ChatGroupId = group.Id, UserId = uid, Role = "member" });
        await _db.SaveChangesAsync();

        return Ok(new { id = group.Id, name = group.Name, memberCount = memberIds.Count + 1 });
    }

    /// <summary>Mes groupes.</summary>
    [HttpGet("mine")]
    public async Task<IActionResult> GetMine()
    {
        var me = User.GetUserId();
        var groupIds = await _db.ChatGroupMembers.Where(m => m.UserId == me).Select(m => m.ChatGroupId).ToListAsync();

        var groups = await _db.ChatGroups.AsNoTracking().Where(g => groupIds.Contains(g.Id)).ToListAsync();
        var result = new List<object>();
        foreach (var g in groups)
        {
            var lastMsg = await _db.ChatGroupMessages.AsNoTracking()
                .Where(m => m.ChatGroupId == g.Id && !m.IsDeleted)
                .OrderByDescending(m => m.CreatedAt).FirstOrDefaultAsync();
            var memberCount = await _db.ChatGroupMembers.CountAsync(m => m.ChatGroupId == g.Id);
            result.Add(new
            {
                id = g.Id,
                name = g.Name,
                photoUrl = g.PhotoUrl,
                isAnnouncementOnly = g.IsAnnouncementOnly,
                isAdmin = await IsAdminAsync(g.Id, me),
                memberCount,
                lastMessage = lastMsg?.Content ?? (lastMsg != null ? "📎 Pièce jointe" : null),
                lastMessageAt = lastMsg?.CreatedAt,
            });
        }
        return Ok(result.OrderByDescending(r => (DateTime?)((dynamic)r).lastMessageAt ?? DateTime.MinValue));
    }

    /// <summary>Membres d'un groupe.</summary>
    [HttpGet("{id:int}/members")]
    public async Task<IActionResult> GetMembers(int id)
    {
        var me = User.GetUserId();
        if (!await IsMemberAsync(id, me)) return StatusCode(403, new { error = "Tu ne fais pas partie de ce groupe." });

        var members = await _db.ChatGroupMembers.AsNoTracking()
            .Include(m => m.User)
            .Where(m => m.ChatGroupId == id)
            .Select(m => new { m.UserId, Name = m.User != null ? $"{m.User.FirstName} {m.User.LastName}".Trim() : null, m.Role, m.User!.AvatarUrl })
            .ToListAsync();
        return Ok(members);
    }

    /// <summary>Messages d'un groupe (les épinglés en tête).</summary>
    [HttpGet("{id:int}/messages")]
    public async Task<IActionResult> GetMessages(int id, [FromQuery] int page = 1, [FromQuery] int pageSize = 30)
    {
        var me = User.GetUserId();
        if (!await IsMemberAsync(id, me)) return StatusCode(403, new { error = "Tu ne fais pas partie de ce groupe." });
        if (pageSize > 100) pageSize = 100;

        var messages = await _db.ChatGroupMessages.AsNoTracking()
            .Include(m => m.Sender)
            .Where(m => m.ChatGroupId == id)
            .OrderByDescending(m => m.CreatedAt)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .ToListAsync();

        var mapped = messages.Select(m => new
        {
            id = m.Id,
            senderId = m.SenderId,
            senderName = m.Sender != null ? $"{m.Sender.FirstName} {m.Sender.LastName}".Trim() : null,
            content = m.IsDeleted ? null : m.Content,
            type = m.Type,
            fileUrl = m.IsDeleted ? null : m.FileUrl,
            fileName = m.FileName,
            isDeleted = m.IsDeleted,
            isPinned = m.IsPinned,
            isFromMe = m.SenderId == me,
            sentAt = m.CreatedAt,
        }).OrderBy(m => m.sentAt);

        return Ok(mapped);
    }

    /// <summary>Poste un message dans le groupe (bloqué pour un membre si le mode "Canal d'annonce" est actif).</summary>
    [HttpPost("{id:int}/messages")]
    public async Task<IActionResult> SendMessage(int id, [FromBody] SendGroupMessageRequest req)
    {
        var me = User.GetUserId();
        if (!await IsMemberAsync(id, me)) return StatusCode(403, new { error = "Tu ne fais pas partie de ce groupe." });

        var group = await _db.ChatGroups.FindAsync(id);
        if (group == null) return NotFound();
        if (group.IsAnnouncementOnly && !await IsAdminAsync(id, me))
            return StatusCode(403, new { error = "Ce groupe est un canal d'annonce — seuls les administrateurs peuvent y écrire." });

        if (string.IsNullOrWhiteSpace(req.Content) && string.IsNullOrWhiteSpace(req.FileUrl))
            return BadRequest(new { error = "Message vide : ajoute du texte ou une pièce jointe." });

        var msg = new ChatGroupMessage
        {
            ChatGroupId = id,
            SenderId = me,
            Content = ContentSanitizer.CensorPhoneNumbers(req.Content),
            Type = string.IsNullOrWhiteSpace(req.Type) ? "text" : req.Type,
            FileUrl = req.FileUrl,
            FileName = req.FileName,
        };
        _db.ChatGroupMessages.Add(msg);
        await _db.SaveChangesAsync();
        return Ok(new { id = msg.Id, sentAt = msg.CreatedAt });
    }

    /// <summary>Épingle/désépingle un message (créateur/admin uniquement).</summary>
    [HttpPost("{id:int}/messages/{messageId:int}/pin")]
    public async Task<IActionResult> TogglePin(int id, int messageId)
    {
        var me = User.GetUserId();
        if (!await IsAdminAsync(id, me)) return StatusCode(403, new { error = "Seul un administrateur du groupe peut épingler un message." });

        var msg = await _db.ChatGroupMessages.FirstOrDefaultAsync(m => m.Id == messageId && m.ChatGroupId == id);
        if (msg == null) return NotFound();
        msg.IsPinned = !msg.IsPinned;
        await _db.SaveChangesAsync();
        return Ok(new { isPinned = msg.IsPinned });
    }

    /// <summary>Active/désactive le mode "Canal d'annonce" (créateur/admin uniquement).</summary>
    [HttpPut("{id:int}/announcement-mode")]
    public async Task<IActionResult> SetAnnouncementMode(int id, [FromBody] SetAnnouncementModeRequest req)
    {
        var me = User.GetUserId();
        if (!await IsAdminAsync(id, me)) return StatusCode(403, new { error = "Seul un administrateur du groupe peut changer ce paramètre." });

        var group = await _db.ChatGroups.FindAsync(id);
        if (group == null) return NotFound();
        group.IsAnnouncementOnly = req.Enabled;
        await _db.SaveChangesAsync();
        return Ok(new { isAnnouncementOnly = group.IsAnnouncementOnly });
    }
}
