using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

public class ExamCoachPlan
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(100)]
    public string ExamType { get; set; } = string.Empty;

    [Required]
    public DateTime ExamDate { get; set; }

    public float HoursPerDay { get; set; } = 2.0f;

    [Required]
    [Column(TypeName = "jsonb")]
    public string PlanJson { get; set; } = "{}";

    public float ConfidenceScore { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? LastRecalibratedAt { get; set; }

    public bool IsActive { get; set; } = true;

    // Navigation
    public User User { get; set; } = null!;
    public ICollection<ExamCoachDayCompletion> DayCompletions { get; set; } = new List<ExamCoachDayCompletion>();
}
