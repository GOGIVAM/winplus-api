using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Avis sur une séance de cours particulier effectuée (US-REP-08). Lot 9.5
/// (17.12, 17.16) : peuvent noter les élèves participants ET le payeur (parent
/// ou institution) ; un seul avis par séance et par personne (index unique
/// TutorBookingId + StudentUserId). Pas d'avis anonyme, pas d'avis sans séance
/// effectuée.
/// </summary>
public class TutorReview
{
    public int Id { get; set; }

    public int TutorBookingId { get; set; }
    public int TutorProfileId { get; set; }

    /// <summary>
    /// Auteur de l'avis. Nom historique (avant le 9.5, seul l'élève notait) :
    /// depuis le 9.5, ce peut être un participant ou le payeur, voir <see cref="ReviewerRole"/>.
    /// </summary>
    public int StudentUserId { get; set; }

    /// <summary>participant | payer ; null = avis antérieur au 9.5 (élève participant).</summary>
    [MaxLength(20)]
    public string? ReviewerRole { get; set; }

    /// <summary>
    /// Lot 9.5 (17.17) : institution payeuse au nom de laquelle l'avis est
    /// donné (compte institution). Un seul avis par séance et par institution,
    /// quel que soit le compte de l'institution (index unique partiel
    /// UX_TutorReviews_Booking_Institution). Null pour les autres auteurs.
    /// </summary>
    public int? ReviewerInstitutionId { get; set; }

    [Range(1, 5)]
    public int Rating { get; set; }

    [MaxLength(300)]
    public string? Comment { get; set; }

    [MaxLength(500)]
    public string? TutorReply { get; set; }
    public DateTime? TutorRepliedAt { get; set; }

    /// <summary>Signalement d'avis abusif (référentiel §I "Signalement d'un avis abusif possible").</summary>
    public bool IsReported { get; set; }
    [MaxLength(500)]
    public string? ReportReason { get; set; }
    public int? ReportedByUserId { get; set; }
    public DateTime? ReportedAt { get; set; }

    /// <summary>
    /// Lot 9.7a : décision de l'administrateur sur un avis signalé. Un avis
    /// retiré (true) n'apparaît plus sur le profil et ne compte plus dans la
    /// note moyenne ; la ligne reste en base (l'auteur ne peut pas renoter la séance).
    /// </summary>
    public bool IsHiddenByModeration { get; set; }
    /// <summary>kept | removed ; null tant que le signalement n'est pas traité.</summary>
    [MaxLength(20)]
    public string? ModerationDecision { get; set; }
    public int? ModeratedByUserId { get; set; }
    public DateTime? ModeratedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TutorBookingId))]
    public TutorBooking? TutorBooking { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }
}
