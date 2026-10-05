namespace Backend.Models.Entities;

/// <summary>
/// Grille de commission catalogue (Module 7, §7B) : ligne unique (Id=1),
/// modifiable par un administrateur via AdminCatalogCommissionController.
/// La part retenue par la plateforme reste toujours bornée entre
/// MinRatePercent et MaxRatePercent, et ces deux bornes elles-mêmes restent
/// toujours dans l'intervalle 10%-60% fixé par le product owner (contrôlé
/// côté service, pas seulement par la valeur par défaut posée ici).
/// </summary>
public class CatalogCommissionSettings
{
    public int Id { get; set; }
    public decimal MinRatePercent { get; set; } = 10;
    public decimal MaxRatePercent { get; set; } = 60;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public int? UpdatedByUserId { get; set; }
}
