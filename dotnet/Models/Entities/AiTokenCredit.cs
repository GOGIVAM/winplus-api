using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Lot 9.7b (18.17, décision 8.5) : tokens WinAI achetés par le portefeuille
/// (recharge) ou offerts par une promotion (bonus_tokens). Ils forment une
/// réserve consommée seulement quand les limites de session ou de semaine du
/// plan sont atteintes ; les messages servis sur cette réserve sont marqués
/// <see cref="AiTokenUsage.FromTopUp"/> et ne comptent plus dans les fenêtres.
/// </summary>
public class AiTokenCredit
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public long Tokens { get; set; }

    [Column(TypeName = "numeric(12,0)")]
    public decimal PricePaidXaf { get; set; }

    /// <summary>topup (achat) ou promotion (bonus offert).</summary>
    [MaxLength(20)]
    public string Origin { get; set; } = "topup";

    public int? PromoCodeId { get; set; }

    /// <summary>Clé d'idempotence de l'achat (une seule écriture par demande).</summary>
    [MaxLength(80)]
    public string IdempotencyKey { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
