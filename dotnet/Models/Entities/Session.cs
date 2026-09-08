namespace Backend.Models.Entities;

/// <summary>
/// Session d'enseignement en ligne — live, enregistrement ou correction
/// (Module 5). Distincte de TutorBooking (Module 1/6, cours particulier
/// 1-à-1) : une Session est ouverte à plusieurs élèves inscrits.
/// </summary>
public class Session
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string? Description { get; set; }

    /// <summary>live | recording | correction (US-SES-01/02, code couleur calendrier).</summary>
    public string Type { get; set; } = "live";

    /// <summary>Matière en texte libre (comme le reste du tableau de bord professeur, ex. MATIERES.id côté front) — pas de FK vers le catalogue.</summary>
    public string? Subject { get; set; }

    public string? Level { get; set; }

    public DateTime StartDate { get; set; }

    public DateTime EndDate { get; set; }

    /// <summary>Dérivé de StartDate/EndDate à la création, gardé pour affichage direct (30-180 min, pas de 15).</summary>
    public int DurationMinutes { get; set; } = 60;

    public int? MaxParticipants { get; set; }

    public bool IsFree { get; set; } = true;

    public decimal? PriceXaf { get; set; }

    /// <summary>Lien externe (Meet/Zoom) — WinPlus n'héberge pas l'appel elle-même.</summary>
    public string? ExternalLink { get; set; }

    /// <summary>scheduled | ongoing | completed | cancelled</summary>
    public string Status { get; set; } = "scheduled";

    public int? CreatedBy { get; set; }

    public DateTime? CancelledAt { get; set; }

    public int? CancelledByUserId { get; set; }

    public string? CancellationReason { get; set; }

    /// <summary>
    /// Transcription collée manuellement par le professeur (US-SES-04) : pas
    /// de pipeline audio/vidéo dans ce projet (le live se passe sur un lien
    /// externe, WinPlus n'enregistre rien) — WinAI résume ce texte plutôt que
    /// de "transcrire" automatiquement un flux qui n'existe pas côté serveur.
    /// </summary>
    public string? TranscriptText { get; set; }

    /// <summary>Résumé structuré généré par WinAI à partir de TranscriptText, éditable avant envoi.</summary>
    public string? SummaryText { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
    public bool IsDeleted { get; set; } = false;

    public ICollection<SessionEnrollment> Enrollments { get; set; } = new List<SessionEnrollment>();
}
