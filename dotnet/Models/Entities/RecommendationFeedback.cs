using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class RecommendationFeedback
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int SubjectId { get; set; }

    [Required]
    [MaxLength(30)]
    public string FeedbackType { get; set; } = string.Empty; // "not_interested" | "already_seen"

    [MaxLength(50)]
    public string Context { get; set; } = "dashboard";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
