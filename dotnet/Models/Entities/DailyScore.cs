using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class DailyScore
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public DateOnly Date { get; set; }

    [Required]
    public decimal AverageScore { get; set; }

    [Required]
    public int QuizCount { get; set; } = 1;

    public int? SubjectId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
