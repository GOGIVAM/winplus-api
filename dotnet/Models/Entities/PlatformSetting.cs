using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Réglage de plateforme persisté (lot 9.7a, suivi 18.2). La liste des
/// réglages, leur type, leurs bornes et leur valeur par défaut sont définis
/// dans le code (<see cref="Backend.Services.PlatformSettingDefinitions"/>) :
/// seule la valeur choisie par un administrateur est stockée ici. Une clé
/// absente de la table vaut sa valeur par défaut.
/// </summary>
public class PlatformSetting
{
    /// <summary>Clé du réglage, par exemple <c>winai.chat.temperature</c>.</summary>
    [MaxLength(100)]
    public string Key { get; set; } = null!;

    /// <summary>Valeur sérialisée en texte invariant (true/false, 0.7, 2000, texte libre).</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>bool | int | decimal | string.</summary>
    [MaxLength(20)]
    public string ValueType { get; set; } = "string";

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int? UpdatedByUserId { get; set; }
}
