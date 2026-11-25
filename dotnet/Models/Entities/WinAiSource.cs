namespace Backend.Models.Entities;

/// <summary>
/// Atelier WinAI : document personnel gardé comme source de WinAI. Le fichier est dans le stockage
/// privé (référence opaque <see cref="StorageRef"/>, jamais une URL), ses extraits dans le RAG personnel
/// de l'utilisateur sous <see cref="DocId"/>. Schéma posé par Migrations/SQL_WinAiSources.sql.
/// </summary>
public class WinAiSource
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "application/octet-stream";
    public long SizeBytes { get; set; }
    /// <summary>Référence du stockage privé (« s3:… » ou « local:… »).</summary>
    public string StorageRef { get; set; } = string.Empty;
    /// <summary>Identifiant du document dans le RAG (winsrc_{userId}_{guid}).</summary>
    public string DocId { get; set; } = string.Empty;
    /// <summary>pending (indexation en cours) | ready | failed</summary>
    public string Status { get; set; } = WinAiSourceStatuses.Pending;
    public string? Error { get; set; }
    public int ChunkCount { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? IndexedAt { get; set; }
    /// <summary>Dernière vérification de l'état auprès du service WinAI.</summary>
    public DateTime? CheckedAt { get; set; }

    public User? User { get; set; }
}

public static class WinAiSourceStatuses
{
    public const string Pending = "pending";
    public const string Ready = "ready";
    public const string Failed = "failed";
}
