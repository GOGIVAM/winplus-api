using System.ComponentModel.DataAnnotations;

namespace Backend.Models.Entities;

/// <summary>
/// Modèle de message réutilisable (US-MSG-08, Module 7). Le texte peut contenir
/// des variables {{NomEleve}}, {{DateSession}}, {{Matière}} — la substitution
/// se fait côté client au moment de l'insertion dans le composeur.
/// </summary>
public class MessageTemplate
{
    public int Id { get; set; }
    public int UserId { get; set; }

    [Required, MaxLength(60)]
    public required string Name { get; set; }

    [Required, MaxLength(1000)]
    public required string Text { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
