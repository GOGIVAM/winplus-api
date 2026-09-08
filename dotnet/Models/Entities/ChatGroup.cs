using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Groupe de messagerie (US-MSG-03, Module 7) : classe, groupe de révision ou
/// collègues. Distinct de <see cref="DirectMessage"/> (1-1) et de
/// <see cref="CourseChannelMessage"/> (canal public par formation, US-MSG-04).
/// </summary>
public class ChatGroup
{
    public int Id { get; set; }
    public int CreatorId { get; set; }

    [Required, MaxLength(80)]
    public required string Name { get; set; }

    [MaxLength(500)]
    public string? PhotoUrl { get; set; }

    /// <summary>Seul le créateur (et les admins) peut poster ; les membres ne peuvent que réagir.</summary>
    public bool IsAnnouncementOnly { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(CreatorId))]
    public User? Creator { get; set; }

    public ICollection<ChatGroupMember> Members { get; set; } = new List<ChatGroupMember>();
}

/// <summary>Appartenance à un groupe de messagerie. admin | member.</summary>
public class ChatGroupMember
{
    public int Id { get; set; }
    public int ChatGroupId { get; set; }
    public int UserId { get; set; }

    [MaxLength(20)]
    public string Role { get; set; } = "member";

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ChatGroupId))]
    public ChatGroup? ChatGroup { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}

/// <summary>Message posté dans un groupe de messagerie.</summary>
public class ChatGroupMessage
{
    public int Id { get; set; }
    public int ChatGroupId { get; set; }
    public int SenderId { get; set; }

    [MaxLength(2000)]
    public string? Content { get; set; }

    /// <summary>text | image | pdf | voice.</summary>
    [MaxLength(20)]
    public string Type { get; set; } = "text";

    [MaxLength(500)]
    public string? FileUrl { get; set; }

    [MaxLength(255)]
    public string? FileName { get; set; }

    public bool IsPinned { get; set; } = false;
    public bool IsDeleted { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(ChatGroupId))]
    public ChatGroup? ChatGroup { get; set; }

    [ForeignKey(nameof(SenderId))]
    public User? Sender { get; set; }
}
