namespace Backend.Services;

/// <summary>
/// Lot 9.7b (droit catalog.preview_pages, étude 1.2 et section 3) : nombre de
/// pages d'aperçu réellement servies pour un contenu payant non acquis.
/// Règle : au plus le droit du plan, et toujours au moins une page de moins
/// que le document (un aperçu ne livre jamais le document entier ; un document
/// d'une page n'a donc pas d'aperçu). Nombre total inconnu (0) : le droit,
/// sous réserve du contrôle qpdf qui exige une page après l'aperçu
/// (<see cref="IDocumentWatermarkService.StampPreviewAsync"/>).
/// </summary>
public static class CatalogPreview
{
    public static int ServedPages(int allowed, int totalPages)
    {
        if (allowed <= 0) return 0;
        if (totalPages <= 0) return allowed;
        return Math.Max(0, Math.Min(allowed, totalPages - 1));
    }
}
