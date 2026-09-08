namespace Backend.Models.DTOs;

public class CreateSessionRequestDto
{
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    /// <summary>live | recording | correction</summary>
    public string Type { get; set; } = "live";
    public string? Subject { get; set; }
    public string? Level { get; set; }
    public DateTime StartDate { get; set; }
    /// <summary>30 à 180, pas de 15 (US-SES-01).</summary>
    public int DurationMinutes { get; set; } = 60;
    public int? MaxParticipants { get; set; }
    public bool IsFree { get; set; } = true;
    public decimal? PriceXaf { get; set; }
    public string? ExternalLink { get; set; }
}

public class TeachingSessionDto
{
    public int Id { get; set; }
    public string Title { get; set; } = null!;
    public string? Description { get; set; }
    public string Type { get; set; } = null!;
    public string? Subject { get; set; }
    public string? Level { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public int DurationMinutes { get; set; }
    public int? MaxParticipants { get; set; }
    public bool IsFree { get; set; }
    public decimal? PriceXaf { get; set; }
    public string? ExternalLink { get; set; }
    public string Status { get; set; } = null!;
    public string? CancellationReason { get; set; }
    public int EnrolledCount { get; set; }
    public decimal RevenueXaf { get; set; }
    public string? TranscriptText { get; set; }
    public string? SummaryText { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class CancelSessionRequestDto
{
    public string? Reason { get; set; }
}

public class EnrollSessionRequestDto
{
    /// <summary>Requis si la session est payante : numéro Mobile Money.</summary>
    public string? Phone { get; set; }
}

public class GenerateSessionSummaryRequestDto
{
    public string TranscriptText { get; set; } = null!;
}

public class UpdateSessionSummaryRequestDto
{
    public string SummaryText { get; set; } = null!;
}
