using Backend.Data;
using Backend.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services.TempsReel;

/// <summary>
/// Publication des événements temps réel de la messagerie. Les événements ne portent que des identifiants :
/// le client recharge la conversation ou la liste par l'API existante. Une erreur de publication ne doit
/// jamais faire échouer l'action de l'utilisateur (le message est déjà enregistré) : elle est seulement journalisée.
/// </summary>
public interface ITempsReel
{
    /// <summary>Nouveau message privé (envoyé aux deux personnes, pour les autres onglets de l'expéditeur).</summary>
    Task MessagePriveAsync(int expediteurId, int destinataireId, int messageId);
    /// <summary>Message privé supprimé ou réaction changée : la conversation est à recharger.</summary>
    Task ConversationModifieeAsync(int userA, int userB, int messageId, string raison);
    /// <summary>Messages d'une conversation lus par <paramref name="lecteurId"/> (accusé de lecture pour l'autre).</summary>
    Task MessagesLusAsync(int lecteurId, int autreId);
    /// <summary>Nouveau message (ou épinglage) dans un groupe de discussion, pour tous ses membres.</summary>
    Task MessageGroupeAsync(int groupeId, int expediteurId, int messageId, string raison = "nouveau");
    /// <summary>Lot 3 : nouveau message dans le canal d'une formation (dont la réponse de WinAI).</summary>
    Task CanalMessageAsync(int courseId, int messageId, string raison = "nouveau");
    /// <summary>Lot 3 : nouvelle réponse dans un sujet du forum.</summary>
    Task ForumReponseAsync(int threadId, int postId);
}

public class TempsReelService : ITempsReel
{
    private readonly IHubContext<TempsReelHub> _hub;
    private readonly ApplicationDbContext _db;
    private readonly ILogger<TempsReelService> _logger;

    public TempsReelService(IHubContext<TempsReelHub> hub, ApplicationDbContext db, ILogger<TempsReelService> logger)
    {
        _hub = hub; _db = db; _logger = logger;
    }

    private static string U(int id) => TempsReelHub.GroupeUtilisateur(id);

    private async Task Envoyer(Func<Task> envoi, string evenement)
    {
        try { await envoi(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Temps réel : échec de publication {Evenement}", evenement); }
    }

    public Task MessagePriveAsync(int expediteurId, int destinataireId, int messageId) =>
        Envoyer(() => _hub.Clients.Groups(U(expediteurId), U(destinataireId))
            .SendAsync("message-prive", new { de = expediteurId, a = destinataireId, id = messageId }), "message-prive");

    public Task ConversationModifieeAsync(int userA, int userB, int messageId, string raison) =>
        Envoyer(() => _hub.Clients.Groups(U(userA), U(userB))
            .SendAsync("conversation-modifiee", new { entre = new[] { userA, userB }, id = messageId, raison }), "conversation-modifiee");

    public Task MessagesLusAsync(int lecteurId, int autreId) =>
        Envoyer(() => _hub.Clients.Groups(U(autreId), U(lecteurId))
            .SendAsync("messages-lus", new { par = lecteurId, avec = autreId }), "messages-lus");

    public Task CanalMessageAsync(int courseId, int messageId, string raison = "nouveau") =>
        Envoyer(() => _hub.Clients.Group(TempsReelHub.GroupeCanal(courseId))
            .SendAsync("canal-message", new { courseId, id = messageId, raison }), "canal-message");

    public Task ForumReponseAsync(int threadId, int postId) =>
        Envoyer(() => _hub.Clients.Group(TempsReelHub.GroupeSujet(threadId))
            .SendAsync("forum-reponse", new { threadId, id = postId }), "forum-reponse");

    public async Task MessageGroupeAsync(int groupeId, int expediteurId, int messageId, string raison = "nouveau")
    {
        var membres = await _db.ChatGroupMembers.AsNoTracking()
            .Where(m => m.ChatGroupId == groupeId).Select(m => m.UserId).ToListAsync();
        if (membres.Count == 0) return;
        await Envoyer(() => _hub.Clients.Groups(membres.Select(U).ToList())
            .SendAsync("message-groupe", new { groupeId, de = expediteurId, id = messageId, raison }), "message-groupe");
    }
}
