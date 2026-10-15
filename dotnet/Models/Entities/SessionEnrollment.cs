using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>Inscription d'un élève à une Session (Module 5).</summary>
public class SessionEnrollment
{
    public int Id { get; set; }

    public int SessionId { get; set; }

    public int StudentId { get; set; }

    public DateTime EnrolledAt { get; set; } = DateTime.UtcNow;

    /// <summary>free | pending | paid | refunded</summary>
    public string PaymentStatus { get; set; } = "free";

    public decimal? PriceChargedXaf { get; set; }

    public string? NotchpayReference { get; set; }

    /// <summary>Lot 9.7b (18.15) : part du professeur figée au paiement de l'inscription.</summary>
    [Column(TypeName = "numeric(5,4)")]
    public decimal? TeacherShareAtPayment { get; set; }

    [ForeignKey(nameof(SessionId))]
    public Session? Session { get; set; }

    [ForeignKey(nameof(StudentId))]
    public User? Student { get; set; }
}
