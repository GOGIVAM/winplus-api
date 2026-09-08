using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Demande de retrait Mobile Money du solde WinPlus (prompt_prof.md Module 7).
/// Comme pour les remboursements de cours particuliers (voir
/// TutorBookingService.SimulateRefundAsync), aucun virement NotchPay
/// automatisé n'existe dans ce projet — INotchPayService ne sait qu'encaisser
/// (InitiatePaymentAsync), pas décaisser. La demande est donc enregistrée et
/// réservée sur le solde immédiatement (empêche le double retrait), puis
/// traitée manuellement par l'admin via Mobile Money — voir WithdrawalsController.
/// </summary>
public class Withdrawal
{
    public int Id { get; set; }
    public int UserId { get; set; }

    /// <summary>mtn | orange.</summary>
    [MaxLength(20)]
    public string Operator { get; set; } = "mtn";

    [MaxLength(30)]
    public string Phone { get; set; } = null!;

    public decimal AmountXaf { get; set; }

    /// <summary>pending (réservé, en attente de virement manuel) | completed | failed (fonds relibérés).</summary>
    [MaxLength(20)]
    public string Status { get; set; } = "pending";

    [MaxLength(300)]
    public string? AdminNote { get; set; }

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }
}
