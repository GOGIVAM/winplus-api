namespace Backend.Models.Entities;

/// <summary>
/// Passage d'une personne dans la salle d'un cours en direct (lot 4 du plan temps réel). Une ligne par entrée
/// dans la salle : la page signale l'arrivée, puis « toujours là » chaque minute, puis le départ. Un onglet
/// fermé brutalement garde son dernier signal (<see cref="LastSeenAt"/>) comme heure de départ.
/// Schéma posé par Migrations/SQL_Visio.sql.
/// </summary>
public class VisioPresence
{
    public int Id { get; set; }
    /// <summary>seance | tutorat</summary>
    public string Type { get; set; } = "seance";
    /// <summary>Id de la Session (seance) ou de la TutorBooking (tutorat).</summary>
    public int RefId { get; set; }
    public int UserId { get; set; }
    public bool IsHost { get; set; }
    public DateTime JoinedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
    public DateTime? LeftAt { get; set; }

    public User? User { get; set; }
}

/// <summary>
/// Enregistrement d'un cours en direct, fait par l'animateur dans Jitsi (enregistrement local) puis déposé
/// dans le stockage privé S3 (préfixe private/visio/). Lisible par l'animateur et les participants de la
/// séance, par lien temporaire. Schéma posé par Migrations/SQL_Visio.sql.
/// </summary>
public class VisioEnregistrement
{
    public int Id { get; set; }
    public string Type { get; set; } = "seance";
    public int RefId { get; set; }
    public int UploadedByUserId { get; set; }
    /// <summary>Clé S3 sous private/visio/ (jamais une URL).</summary>
    public string StorageKey { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string ContentType { get; set; } = "video/webm";
    public long SizeBytes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsDeleted { get; set; }
}
