using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

public class WebhookIdempotencyKey
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int Id { get; set; }

    [Required]
    [MaxLength(255)]
    public string EventId { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Provider { get; set; } = "notchpay";

    [MaxLength(50)]
    public string? EventType { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
