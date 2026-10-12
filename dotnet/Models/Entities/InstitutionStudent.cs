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

    /// <summary>
    /// Confirmation du rattachement par l'élève (suivi 17.14) : pending |
    /// accepted | refused (voir <see cref="InstitutionAttachmentStatuses"/>).
    /// Un rattachement créé par import (CSV ou ajout manuel) est en attente
    /// tant que l'élève n'a pas répondu. Seul le tutorat (réserver, payer,
    /// attribuer un tuteur) exige « accepted » ; les autres usages du
    /// rattachement (annuaire, KPIs, rapports, messagerie) n'en dépendent pas.
    /// </summary>
    [MaxLength(20)]
    public string ConfirmationStatus { get; set; } = InstitutionAttachmentStatuses.Pending;

    /// <summary>Date de la dernière réponse de l'élève (acceptation ou refus).</summary>
    public DateTime? RespondedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(InstitutionId))]
    public Institution? Institution { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}

/// <summary>Statuts de confirmation d'un rattachement élève-institution (suivi 17.14).</summary>
public static class InstitutionAttachmentStatuses
{
    public const string Pending = "pending";
    public const string Accepted = "accepted";
    public const string Refused = "refused";
}
