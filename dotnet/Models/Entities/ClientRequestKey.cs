using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Écriture déjà traitée pour un en-tête Idempotency-Key (mode hors ligne du
/// mobile, lot 3) : une action rejouée par la file d'attente du téléphone,
/// dont la première réponse s'est perdue, reçoit cette réponse au lieu
/// d'être exécutée une seconde fois. Schéma : Migrations/SQL_ClientRequestKeys.sql.
/// </summary>
public class ClientRequestKey
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string Key { get; set; } = string.Empty;

    [Required]
    [MaxLength(10)]
    public string Method { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string Path { get; set; } = string.Empty;

    public int StatusCode { get; set; }

    /// <summary>Corps JSON de la première réponse (64 Ko au plus, sinon null).</summary>
    public string? ResponseBody { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
