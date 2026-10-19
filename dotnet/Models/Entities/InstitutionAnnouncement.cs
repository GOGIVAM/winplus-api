using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Annonce d'un établissement à ses élèves et/ou aux parents de ses élèves,
/// pour tout l'établissement ou une classe. Schéma posé par
/// Migrations/SQL_InstitutionAnnouncements.sql.
/// </summary>
public class InstitutionAnnouncement
{
    public int Id { get; set; }

    public int InstitutionId { get; set; }

    /// <summary>Classe visée (StudyGroup du compte institution) ; null : tout l'établissement.</summary>
    public int? StudyGroupId { get; set; }

    public int AuthorUserId { get; set; }

    /// <summary>students | parents | all.</summary>
    [MaxLength(20)]
    public string Audience { get; set; } = InstitutionAnnouncementAudiences.All;

    /// <summary>info | important | urgent.</summary>
    [MaxLength(20)]
    public string Type { get; set; } = "info";

    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    /// <summary>Nombre de destinataires au moment de l'envoi.</summary>
    public int RecipientCount { get; set; }

    public DateTime? ExpiresAt { get; set; }

    public bool IsDeleted { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public Institution? Institution { get; set; }
    public StudyGroup? StudyGroup { get; set; }
}

public static class InstitutionAnnouncementAudiences
{
    public const string Students = "students";
    public const string Parents = "parents";
    public const string All = "all";

    public static bool IsValid(string? v) => v is Students or Parents or All;
    public static bool ForStudents(string a) => a is Students or All;
    public static bool ForParents(string a) => a is Parents or All;
}
