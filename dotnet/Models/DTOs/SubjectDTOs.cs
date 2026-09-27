using System.ComponentModel.DataAnnotations;

namespace Backend.Models.DTOs;

/// <summary>
/// Champs autorisés à la création d'une matière (Module 17).
///
/// Le contrôleur liait auparavant directement l'entité `Subject` au corps de
/// la requête, ce qui permettait d'écrire n'importe quel champ : état de
/// publication, compteurs d'inscription, note moyenne, mise en avant,
/// suppression logique, et surtout l'auteur du contenu (qui détermine à qui
/// revient le revenu de la vente). Cette liste blanche ferme cette écriture
/// massive : tout champ absent d'ici n'est plus pilotable par le client.
///
/// `IsPublished` est volontairement absent : la publication reste une
/// décision humaine passant par l'endpoint d'approbation administrateur
/// (POST /api/admin/subjects/{id}/approve).
/// </summary>
public class SubjectCreateRequest
{
    [Required]
    [StringLength(300, MinimumLength = 2)]
    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    [StringLength(150)]
    public string? Category { get; set; }

    [StringLength(150)]
    public string? Level { get; set; }

    [StringLength(1000)]
    public string? ThumbnailUrl { get; set; }

    /// <summary>
    /// Prix de vente en FCFA. Devise sans sous-unité : arrondi à zéro
    /// décimale côté serveur, jamais conservé fractionnaire.
    /// </summary>
    [Range(0, 100_000_000)]
    public decimal Price { get; set; }
}

/// <summary>
/// Champs autorisés à la mise à jour d'une matière (Module 17).
///
/// Sémantique retenue, pour ne casser aucun appelant existant :
/// - `Title` et `Price` laissés à null sont conservés tels quels (ces deux
///   champs ne peuvent pas être « vidés », les omettre revient à ne pas y
///   toucher) ;
/// - `Description`, `Category`, `Level` et `ThumbnailUrl` sont appliqués tels
///   qu'envoyés, y compris null, car l'écran d'administration du catalogue
///   s'appuie sur cette possibilité pour retirer un niveau déjà taggé.
/// Les champs absents de cette liste (publication, compteurs, note, auteur,
/// mise en avant, suppression logique) ne sont plus modifiables par le client.
/// </summary>
public class SubjectUpdateRequest
{
    [StringLength(300, MinimumLength = 2)]
    public string? Title { get; set; }

    public string? Description { get; set; }

    [StringLength(150)]
    public string? Category { get; set; }

    [StringLength(150)]
    public string? Level { get; set; }

    [StringLength(1000)]
    public string? ThumbnailUrl { get; set; }

    [Range(0, 100_000_000)]
    public decimal? Price { get; set; }
}
