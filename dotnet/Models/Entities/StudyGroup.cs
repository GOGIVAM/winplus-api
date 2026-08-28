using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Groupe d'étude collaboratif créé par un élève (S7-2).
/// Le code d'invitation est unique et permet de rejoindre le groupe.
/// </summary>
public class StudyGroup
{
    public int Id { get; set; }

    public int OwnerId { get; set; }

    [Required, MaxLength(80)]
    public required string Name { get; set; }

    [MaxLength(100)]
    public string? Subject { get; set; }

    [MaxLength(500)]
    public string? Description { get; set; }

    /// <summary>Code d'invitation à 6 caractères (unique).</summary>
    [Required, MaxLength(10)]
    public required string JoinCode { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastActivityAt { get; set; }

    [ForeignKey(nameof(OwnerId))]
    public User? Owner { get; set; }

    public ICollection<StudyGroupMember> Members { get; set; } = new List<StudyGroupMember>();
}

/// <summary>Appartenance d'un élève à un groupe d'étude.</summary>
public class StudyGroupMember
{
    public int Id { get; set; }

    public int StudyGroupId { get; set; }

    public int UserId { get; set; }

    /// <summary>owner | member</summary>
    [MaxLength(20)]
    public string Role { get; set; } = "member";

    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(StudyGroupId))]
    public StudyGroup? StudyGroup { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
