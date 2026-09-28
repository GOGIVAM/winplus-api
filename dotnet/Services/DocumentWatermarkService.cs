using System.Globalization;
using System.Text;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Backend.Services;

/// <summary>
/// Filigrane nominatif incrusté dans le PDF servi à la visionneuse (Module 44,
/// décisions §11.3/§11.4).
///
/// Le filigrane n'est pas une surcouche CSS ou un widget posé par-dessus le
/// document : il est ajouté côté serveur dans le flux PDF lui-même, sur
/// chaque page, avant l'envoi. Toute capture d'écran, comme tout fichier
/// intercepté sur le réseau, porte donc l'identité du compte qui l'a
/// consulté. On réutilise QuestPDF (déjà présent pour les factures et
/// certificats) et son moteur qpdf, plutôt que d'ajouter une seconde
/// bibliothèque PDF au serveur.
///
/// Le document n'est volontairement pas chiffré (restriction d'impression
/// ou de copie par mot de passe propriétaire) : les visionneuses intégrées
/// ignorent ces drapeaux, et certains moteurs de rendu natifs mobiles
/// refusent d'ouvrir un PDF chiffré. La protection repose sur l'absence
/// de toute URL de fichier côté client et sur le filigrane lui-même.
/// </summary>
public interface IDocumentWatermarkService
{
    /// <summary>
    /// Renvoie une copie de <paramref name="sourcePdf"/> portant
    /// <paramref name="label"/> en filigrane sur chaque page. Lève une
    /// exception si le filigrane n'a pas pu être posé : l'appelant ne doit
    /// jamais servir le document d'origine à la place.
    /// </summary>
    Task<byte[]> StampAsync(Stream sourcePdf, string label, CancellationToken ct = default);
}

public sealed class DocumentWatermarkService : IDocumentWatermarkService
{
    private const int MaxLabelLength = 90;

    public async Task<byte[]> StampAsync(Stream sourcePdf, string label, CancellationToken ct = default)
    {
        var workDir = Path.Combine(Path.GetTempPath(), "winplus-watermark", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(workDir);
        var sourcePath = Path.Combine(workDir, "source.pdf");
        var overlayPath = Path.Combine(workDir, "overlay.pdf");
        var outputPath = Path.Combine(workDir, "output.pdf");

        try
        {
            await using (var file = File.Create(sourcePath))
                await sourcePdf.CopyToAsync(file, ct);

            var safeLabel = SanitizeLabel(label);

            await Task.Run(() =>
            {
                WriteOverlay(overlayPath, safeLabel);

                DocumentOperation
                    .LoadFile(sourcePath)
                    .OverlayFile(new DocumentOperation.LayerConfiguration
                    {
                        FilePath = overlayPath,
                        // Une seule page de filigrane, répétée sur toutes les
                        // pages du document, quel qu'en soit le nombre.
                        RepeatSourcePages = "1",
                    })
                    .Save(outputPath);
            }, ct);

            return await File.ReadAllBytesAsync(outputPath, ct);
        }
        finally
        {
            try { Directory.Delete(workDir, recursive: true); }
            catch { /* nettoyage au mieux : répertoire temporaire du système */ }
        }
    }

    /// <summary>
    /// Page A4 transparente : texte en diagonale répété sur toute la surface,
    /// plus une ligne de pied de page lisible. qpdf la centre sur chaque page
    /// et la réduit si la page est plus petite, sans l'agrandir : sur un
    /// format plus grand que l'A4 (A3 paysage), le filigrane couvre la zone
    /// centrale de la page plutôt que toute sa surface.
    /// </summary>
    private static void WriteOverlay(string path, string label)
    {
        try
        {
            BuildOverlay(label).GeneratePdf(path);
        }
        catch
        {
            // Un nom contenant des glyphes absents de toutes les polices
            // disponibles ne doit pas empêcher la consultation : on retombe
            // sur une version ASCII du même libellé plutôt que d'échouer.
            BuildOverlay(ToAscii(label)).GeneratePdf(path);
        }
    }

    private static IDocument BuildOverlay(string label)
    {
        var line = string.Join("        ", Enumerable.Repeat(label, 5));
        var footer = $"Document personnel consulté par {label} · reproduction et diffusion interdites";

        return Document.Create(container => container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.Margin(0);
            page.PageColor(Colors.Transparent);

            page.Content().Layers(layers =>
            {
                // Diagonale montante : origine placée hors page (en haut à
                // gauche) pour que les lignes couvrent tout le format une
                // fois tournées, coins compris.
                layers.PrimaryLayer()
                    .OffsetX(-400)
                    .OffsetY(150)
                    .Unconstrained()
                    .Rotate(-30)
                    .Column(col =>
                    {
                        col.Spacing(64);
                        for (var i = 0; i < 18; i++)
                            col.Item().Text(line).FontSize(15).SemiBold().FontColor("#2E1E293B");
                    });

                layers.Layer()
                    .AlignBottom()
                    .PaddingBottom(8)
                    .AlignCenter()
                    .Background("#B3FFFFFF")
                    .PaddingHorizontal(6)
                    .PaddingVertical(2)
                    .Text(footer).FontSize(7).FontColor("#99334155");
            });
        }));
    }

    /// <summary>
    /// Libellé neutralisé : sans caractères de contrôle, sans marques de
    /// direction ni caractères invisibles (qui permettraient de masquer ou
    /// renverser le texte affiché), espaces normalisés, longueur bornée.
    /// </summary>
    public static string SanitizeLabel(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return "WinPlus";

        var sb = new StringBuilder(label.Length);
        var previousSpace = false;
        foreach (var ch in label.Normalize(NormalizationForm.FormC))
        {
            var cat = CharUnicodeInfo.GetUnicodeCategory(ch);
            if (cat is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse
                or UnicodeCategory.OtherNotAssigned
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
                continue;

            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace && sb.Length > 0) sb.Append(' ');
                previousSpace = true;
                continue;
            }

            sb.Append(ch);
            previousSpace = false;
        }

        var result = sb.ToString().Trim();
        if (result.Length > MaxLabelLength) result = result[..MaxLabelLength].TrimEnd() + "…";
        return result.Length == 0 ? "WinPlus" : result;
    }

    private static string ToAscii(string label)
    {
        var decomposed = label.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark) continue;
            sb.Append(ch is >= ' ' and <= '~' ? ch : '?');
        }
        var result = sb.ToString().Trim();
        return result.Length == 0 ? "WinPlus" : result;
    }
}
