using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Avis élève sur une séance de cours particulier effectuée (US-REP-08).
/// Un seul avis par réservation, uniquement pour une séance réellement tenue
/// — pas d'avis anonyme, pas d'avis sans séance confirmée.
/// </summary>
public class TutorReview
{
    public int Id { get; set; }

    public int TutorBookingId { get; set; }
    public int TutorProfileId { get; set; }
    public int StudentUserId { get; set; }

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

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(TutorBookingId))]
    public TutorBooking? TutorBooking { get; set; }

    [ForeignKey(nameof(TutorProfileId))]
    public TutorProfile? TutorProfile { get; set; }

    [ForeignKey(nameof(StudentUserId))]
    public User? Student { get; set; }
}
