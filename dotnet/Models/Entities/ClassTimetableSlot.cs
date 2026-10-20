using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Créneau hebdomadaire de l'emploi du temps d'une classe d'établissement.
/// Heures « HH:mm » à l'heure du Cameroun. Schéma posé par Migrations/SQL_ClassTimetable.sql.
/// </summary>
public class ClassTimetableSlot
{
    public int Id { get; set; }
    public int StudyGroupId { get; set; }

    /// <summary>1 = lundi … 7 = dimanche.</summary>
    public int DayOfWeek { get; set; }

    [MaxLength(5)] public string StartTime { get; set; } = "08:00";
    [MaxLength(5)] public string EndTime { get; set; } = "09:00";
    [MaxLength(100)] public string Subject { get; set; } = string.Empty;
    public int? TeacherUserId { get; set; }
    [MaxLength(50)] public string? Room { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public StudyGroup? StudyGroup { get; set; }
    public User? Teacher { get; set; }
}
