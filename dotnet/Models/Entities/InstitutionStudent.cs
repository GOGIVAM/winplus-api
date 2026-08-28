using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Rattachement d'un élève à une institution, avec son groupe/classe (S5-2).
/// Sert d'annuaire et de base aux KPIs de licences.
/// </summary>
public class InstitutionStudent
{
    public int Id { get; set; }

    public int InstitutionId { get; set; }

    public int StudentId { get; set; }

    [MaxLength(100)]
    public string? GroupName { get; set; }

    [MaxLength(100)]
    public string? Level { get; set; }

    [MaxLength(50)]
    public string? MatriculeNumber { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
