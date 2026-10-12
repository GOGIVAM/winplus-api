namespace Backend.Models.DTOs;

public class SubmitTutorReviewRequestDto
{
    public int TutorBookingId { get; set; }
    public int Rating { get; set; }
    public string? Comment { get; set; }
}

public class ReplyToTutorReviewRequestDto
{
    public string Reply { get; set; } = null!;
}

public class ReportTutorReviewRequestDto
{
    public string Reason { get; set; } = null!;
}

public class TutorReviewDto
{
    public int Id { get; set; }
    public int TutorBookingId { get; set; }
    public string? StudentName { get; set; }
    public string? StudentAvatarUrl { get; set; }
    /// <summary>Lot 9.5 : participant (élève) | payer (parent, institution).</summary>
    public string ReviewerRole { get; set; } = "participant";
    public int Rating { get; set; }
    public string? Comment { get; set; }
    public string? TutorReply { get; set; }
    public DateTime? TutorRepliedAt { get; set; }
    public DateTime CreatedAt { get; set; }
}
