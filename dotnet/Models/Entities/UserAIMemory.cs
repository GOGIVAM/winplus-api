using System;
using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Mémoire persistante de WinAI par étudiant.
/// MemoryType: learning_preference | understood_topics | struggling_topics | exam_context | motivation_style
/// </summary>
public class UserAIMemory
{
    public int Id { get; set; }

    [Required]
    public int UserId { get; set; }

    [Required]
    [MaxLength(50)]
    public string MemoryType { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
