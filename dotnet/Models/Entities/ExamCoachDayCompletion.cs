using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class ExamCoachDayCompletion
{
    public int Id { get; set; }

    [Required]
    public int PlanId { get; set; }

    [Required]
    public int DayNumber { get; set; }

    public float? QuizScore { get; set; }

    public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ExamCoachPlan Plan { get; set; } = null!;
}
