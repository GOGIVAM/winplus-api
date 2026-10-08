using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Module 33 (lot 7) : paramétrage administrateur du déclenchement hybride de
/// l'album de fin d'année. Une ligne par année scolaire, jamais codée en dur
/// ni posée dans un fichier de configuration : un administrateur choisit la
/// date de fin d'année scolaire via l'écran dédié (AdminAlbumSchedule), et
/// YearlyAlbumSchedulerService (service planifié, même patron que
/// WeeklyParentReportService) la lit pour savoir quand générer
/// automatiquement les aperçus.
///
/// Status : "Pending" (date pas encore atteinte) → "PreviewGenerated"
/// (aperçus écrits pour tous les parents éligibles, en attente de validation
/// admin) → "Dispatched" (diffusion confirmée manuellement, les parents
/// voient leur album).
/// </summary>
public class AlbumSchedule
{
    public int Id { get; set; }

    /// <summary>Format canonique "2024-2025" (voir YearlyAlbumService.NormalizeSchoolYear).</summary>
    [Required]
    [MaxLength(20)]
    public string SchoolYear { get; set; } = string.Empty;

    /// <summary>Date (UTC) à laquelle le service planifié doit générer l'aperçu automatiquement.</summary>
    [Column(TypeName = "timestamp with time zone")]
    public DateTime TriggerDate { get; set; }

    /// <summary>Pending | PreviewGenerated | Dispatched</summary>
    [Required]
    [MaxLength(20)]
    public string Status { get; set; } = "Pending";

    public DateTime? GeneratedAt { get; set; }
    public DateTime? DispatchedAt { get; set; }

    /// <summary>Admin ayant configuré la date (traçabilité).</summary>
    public int CreatedByUserId { get; set; }

    /// <summary>Admin ayant confirmé la diffusion réelle.</summary>
    public int? DispatchedByUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    [ForeignKey(nameof(CreatedByUserId))]
    public User? CreatedByUser { get; set; }

    [ForeignKey(nameof(DispatchedByUserId))]
    public User? DispatchedByUser { get; set; }
}
