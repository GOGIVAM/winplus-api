using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>Créneau de rendez-vous publié par un enseignant (Migrations/SQL_ParentTeacherMeetings.sql).</summary>
public class TeacherMeetingSlot
{
    public int Id { get; set; }
    public int TeacherUserId { get; set; }
    public DateTime StartsAt { get; set; }
    public int DurationMinutes { get; set; } = 15;
    /// <summary>presentiel | visio | telephone</summary>
    [MaxLength(20)] public string Mode { get; set; } = "presentiel";
    [MaxLength(200)] public string? Location { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public User? Teacher { get; set; }
}

/// <summary>Rendez-vous réservé par un parent sur un créneau d'enseignant.</summary>
public class ParentTeacherMeeting
{
    public int Id { get; set; }
    public int SlotId { get; set; }
    public int TeacherUserId { get; set; }
    public int ParentUserId { get; set; }
    public int ChildUserId { get; set; }
    [MaxLength(500)] public string? Topic { get; set; }
    /// <summary>booked | cancelled</summary>
    [MaxLength(20)] public string Status { get; set; } = "booked";
    public int? CancelledBy { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CancelledAt { get; set; }
    public TeacherMeetingSlot? Slot { get; set; }
    public User? Parent { get; set; }
    public User? Child { get; set; }
    public User? Teacher { get; set; }
}
