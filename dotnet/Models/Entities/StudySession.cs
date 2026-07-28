using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

public class StudySession
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    public int SubjectId { get; set; }

    [Required]
    public int Duration { get; set; }  // minutes

    public decimal? Score { get; set; }

    public string? KeyPoints { get; set; }  // JSON array of strings

    public DateTime? CompletedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
    public Subject Subject { get; set; } = null!;
}
