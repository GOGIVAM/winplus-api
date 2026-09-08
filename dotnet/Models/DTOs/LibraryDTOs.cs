namespace Backend.Models.DTOs;

/// <summary>
/// Un élément de "Ma bibliothèque" (Module 2, US-CAT-05) : soit un contenu
/// déjà organisé (favori/dossier/notes via Favorite), soit un contenu acheté
/// pas encore organisé. Les Favoris ne sont pas un système séparé : ils SONT
/// le mécanisme d'organisation de la bibliothèque (dossier = FavoriteCollection,
/// notes/tags = Favorite.Notes/Tags). "source" distingue seulement l'origine
/// de l'accès (achat vs simple ajout au cœur), pas deux bibliothèques.
/// </summary>
public class LibraryItemDto
{
    public int SubjectId { get; set; }
    public string? Title { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? Category { get; set; }
    public int? FavoriteId { get; set; }
    public int? CollectionId { get; set; }
    public string? CollectionName { get; set; }
    public List<string> Tags { get; set; } = new();
    public string? Notes { get; set; }
    public DateTime AcquiredAt { get; set; }
    /// <summary>purchased | organized | both</summary>
    public string Source { get; set; } = "purchased";
}

public class OrganizeLibraryItemRequest
{
    public int? CollectionId { get; set; }

    /// <summary>
    /// true = retire explicitement du dossier (glisser-déposer vers "Sans
    /// dossier"). Nécessaire car CollectionId=null est indiscernable de
    /// "champ non fourni" une fois désérialisé sur un int? nullable.
    /// </summary>
    public bool ClearCollection { get; set; }

    public List<string>? Tags { get; set; }
    public string? Notes { get; set; }
}
