namespace Backend.Models.Entities;

/// <summary>
/// Atelier WinAI (25/11/2026) : ce que WinAI a produit pour un élève à partir de ses sources
/// (fiches recto verso, script de résumé audio). Kind : flashcards | resume-audio.
/// ContentJson : {cards:[{front, back, hint}]} ou {script, wordCount, estimatedSeconds} ;
/// SourcesJson : sources choisies [{type, id}] et citations [{type, id, title, page}].
/// Schéma posé par Migrations/SQL_WinAiProductions.sql.
/// </summary>
public class WinAiProduction
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Kind { get; set; } = "flashcards";
    public string Title { get; set; } = string.Empty;
    public string? Subject { get; set; }
    public string? Topic { get; set; }
    public string? Level { get; set; }
    public int ItemCount { get; set; }
    public string ContentJson { get; set; } = "{}";
    public string SourcesJson { get; set; } = "{}";
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }
}
