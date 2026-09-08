using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Archivage d'une conversation directe (Module 7, UX Messagerie mobile :
/// "Swipe gauche → archiver"). Par utilisateur — archiver ne masque le fil
/// que pour soi, jamais pour l'autre participant. Un nouveau message reçu ne
/// désarchive pas automatiquement le fil (comportement WhatsApp standard) ;
/// l'utilisateur le désarchive lui-même s'il veut le retrouver dans la liste
/// principale.
/// </summary>
public class ArchivedConversation
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public int OtherUserId { get; set; }
    public DateTime ArchivedAt { get; set; } = DateTime.UtcNow;

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
