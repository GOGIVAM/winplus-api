using System.Text.Json;

namespace Backend.Services;

/// <summary>
/// Atelier WinAI (25/11/2026) : sources choisies par l'élève pour une génération (quiz, fiche de révision),
/// déjà contrôlées et traduites par IWinAiSourceService.ResolveAsync. Filter et Context partent vers Python
/// (sources_filter, sources_context) ; Citations revient de Python ({doc_id, subject_id, course_id, title, page})
/// et se traduit pour le client avec ResolvedSources.MapCitations.
/// </summary>
public sealed class GenerationSources
{
    public object? Filter { get; init; }
    public string? Context { get; init; }
    public JsonElement? Citations { get; set; }
}
