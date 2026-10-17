using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace Backend.Services;

/// <summary>
/// Nombre de pages d'un PDF, sans bibliothèque supplémentaire (lot 9.7b,
/// aperçu du catalogue : « Aperçu : N pages sur M »).
///
/// Lecture : la plus grande valeur <c>/Count</c> d'un nœud <c>/Type /Pages</c>
/// (la racine de l'arbre des pages), cherchée dans le fichier brut et dans
/// les flux d'objets compressés (<c>/ObjStm</c>, FlateDecode) ; à défaut, le
/// nombre d'objets <c>/Type /Page</c>. Renvoie 0 si rien n'est lisible.
///
/// Ce compte sert à l'affichage et à borner l'aperçu ; il n'est jamais seul
/// garant : le serveur vérifie aussi avec qpdf qu'il reste au moins une page
/// après l'aperçu (<see cref="IDocumentWatermarkService.StampPreviewAsync"/>).
/// </summary>
public static class PdfPageCounter
{
    private static readonly Regex PagesCount = new(@"/Type\s*/Pages\b[^>]*?/Count\s+(\d+)|/Count\s+(\d+)[^>]*?/Type\s*/Pages\b",
        RegexOptions.Compiled | RegexOptions.Singleline);
    private static readonly Regex PageObject = new(@"/Type\s*/Page(?![A-Za-z])", RegexOptions.Compiled);

    public static int Count(byte[] pdf)
    {
        if (pdf == null || pdf.Length < 8) return 0;
        var text = Encoding.Latin1.GetString(pdf);
        var chunks = new List<string> { text };

        // Flux d'objets : on repère chaque mot-clé « stream » et on lit le
        // dictionnaire qui le précède (fenêtre bornée, coût linéaire).
        var pos = 0;
        while ((pos = text.IndexOf("stream", pos, StringComparison.Ordinal)) >= 0)
        {
            var kw = pos;
            pos += 6;
            if (kw >= 3 && string.CompareOrdinal(text, kw - 3, "end", 0, 3) == 0) continue;
            var window = Math.Min(kw, 2048);
            var dictStart = window > 0 ? text.LastIndexOf("<<", kw - 1, window, StringComparison.Ordinal) : -1;
            if (dictStart < 0) continue;
            var dict = text.Substring(dictStart, kw - dictStart);
            if (!dict.Contains("/ObjStm") || !dict.Contains("/FlateDecode")) continue;
            var start = kw + 6;
            if (start < text.Length && text[start] == '\r') start++;
            if (start < text.Length && text[start] == '\n') start++;
            var end = text.IndexOf("endstream", start, StringComparison.Ordinal);
            if (end <= start) continue;
            var inflated = Inflate(pdf, start, end - start);
            if (inflated != null) chunks.Add(inflated);
            pos = end + 9;
        }

        var maxCount = 0;
        var pageObjects = 0;
        foreach (var chunk in chunks)
        {
            foreach (Match m in PagesCount.Matches(chunk))
            {
                var g = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                if (int.TryParse(g, out var n) && n > maxCount) maxCount = n;
            }
            pageObjects += PageObject.Matches(chunk).Count;
        }
        return maxCount > 0 ? maxCount : pageObjects;
    }

    private static string? Inflate(byte[] pdf, int offset, int length)
    {
        try
        {
            using var input = new MemoryStream(pdf, offset, length);
            using var z = new ZLibStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            z.CopyTo(output);
            return Encoding.Latin1.GetString(output.ToArray());
        }
        catch
        {
            return null;
        }
    }
}
