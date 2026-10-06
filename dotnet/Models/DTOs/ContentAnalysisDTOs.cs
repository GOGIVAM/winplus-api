using System.Text.Json.Serialization;

namespace Backend.Models.DTOs;

/// <summary>
/// Module 8 : requête d'analyse synchrone d'un fichier déjà déposé (sur S3,
/// via AdminUploadsController ou son pendant professeur), pour préremplir le
/// formulaire de publication. Noms en snake_case : relayés tels quels au
/// service Python (routes/teacher_extra_routes.py::analyze_content_upload).
/// </summary>
public class AnalyzeContentUploadRequest
{
    [JsonPropertyName("file_url")]
    public string FileUrl { get; set; } = string.Empty;

    [JsonPropertyName("filename")]
    public string? Filename { get; set; }

    /// <summary>epreuve | correction | livre | quiz | pack | formation | video.</summary>
    [JsonPropertyName("content_kind")]
    public string ContentKind { get; set; } = "epreuve";

    /// <summary>
    /// Listes fermées du <select> appelant (ex. matière/niveau côté professeur) :
    /// WinAI choisit EXACTEMENT une valeur dedans plutôt que du texte libre à
    /// rapprocher après coup. Laisser vide pour un champ libre (admin).
    /// </summary>
    [JsonPropertyName("allowed_categories")]
    public List<string>? AllowedCategories { get; set; }

    [JsonPropertyName("allowed_levels")]
    public List<string>? AllowedLevels { get; set; }
}

public class SuggestedContentFieldsDto
{
    public string? Title { get; set; }
    public string? Description { get; set; }

    [JsonPropertyName("description_courte")]
    public string? DescriptionCourte { get; set; }
    public string? Category { get; set; }
    public string? Level { get; set; }
    public string? Difficulty { get; set; }
    public string? Year { get; set; }

    [JsonPropertyName("exam_type")]
    public string? ExamType { get; set; }
    public List<string> Tags { get; set; } = new();
    public List<string> Objectives { get; set; } = new();
    public List<string> Prerequisites { get; set; } = new();

    [JsonPropertyName("duration_seconds")]
    public int? DurationSeconds { get; set; }

    [JsonPropertyName("price_suggestion")]
    public decimal? PriceSuggestion { get; set; }

    public string? Author { get; set; }
    public string? Publisher { get; set; }

    [JsonPropertyName("page_count")]
    public int? PageCount { get; set; }
}

/// <summary>
/// Module 8 (préremplissage) + Module 7 (score de commission)  une seule
/// lecture du fichier sert les deux, voir commentaire du endpoint Python.
/// </summary>
public class AnalyzeContentUploadResponse
{
    public SuggestedContentFieldsDto Suggested { get; set; } = new();

    [JsonPropertyName("winai_score")]
    public decimal? WinAiScore { get; set; }

    [JsonPropertyName("winai_justification")]
    public string? WinAiJustification { get; set; }

    [JsonPropertyName("extraction_warning")]
    public string? ExtractionWarning { get; set; }
}
