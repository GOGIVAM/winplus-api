using Backend.Data;
using Backend.Extensions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace Backend.Hubs;

/// <summary>
/// Temps réel de la messagerie (lot 2 du plan temps réel, 18/11/2026) : une seule connexion par onglet,
/// en WebSocket quand le réseau le permet (sinon SignalR bascule seul en SSE ou en interrogation longue).
///
/// Chaque connexion rejoint le groupe « u:{id} » de son utilisateur. Les contrôleurs publient des événements
/// légers (identifiants seulement) par <see cref="Services.TempsReel.ITempsReel"/> ; le navigateur recharge
/// ensuite par l'API existante, qui garde seule la vérification des droits. Le hub ne fait que relayer
/// « en train d'écrire », après contrôle du lien entre les deux personnes ou de l'appartenance au groupe.
///
/// Le SSE de WinAI (streaming du chat) et celui des notifications restent en place, sans lien avec ce hub.
/// </summary>
[Authorize]
public class TempsReelHub : Hub
{
    public static string GroupeUtilisateur(int userId) => $"u:{userId}";
    public static string GroupeCanal(int courseId) => $"canal:{courseId}";
    public static string GroupeSujet(int threadId) => $"forum:{threadId}";

    private readonly ApplicationDbContext _db;
    public TempsReelHub(ApplicationDbContext db) { _db = db; }

    public override async Task OnConnectedAsync()
    {
        var me = Context.User!.GetUserId();
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupeUtilisateur(me));
        await base.OnConnectedAsync();
    }

    /// <summary>« En train d'écrire » dans une conversation privée : relayé seulement entre deux personnes
    /// qui ont déjà échangé.</summary>
    public async Task EcritPrive(int destinataireId)
    {
        var me = Context.User!.GetUserId();
        if (destinataireId == me) return;
        var lies = await _db.DirectMessages.AsNoTracking().AnyAsync(m =>
            (m.FromUserId == me && m.ToUserId == destinataireId) || (m.FromUserId == destinataireId && m.ToUserId == me));
        if (!lies) return;
        await Clients.Group(GroupeUtilisateur(destinataireId)).SendAsync("ecrit", new { type = "prive", de = me });
    }

    /// <summary>Lot 3 : rejoint le canal d'une formation (enseignant ou élève inscrit seulement) tant que
    /// l'écran du canal est ouvert. Renvoie false si l'accès est refusé.</summary>
    public async Task<bool> RejoindreCanal(int courseId)
    {
        var me = Context.User!.GetUserId();
        var course = await _db.Courses.AsNoTracking().Where(c => c.Id == courseId).Select(c => new { c.InstructorId }).FirstOrDefaultAsync();
        if (course == null) return false;
        var autorise = course.InstructorId == me
            || await _db.CourseEnrollments.AsNoTracking().AnyAsync(e => e.CourseId == courseId && e.UserId == me && e.IsActive);
        if (!autorise) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupeCanal(courseId));
        return true;
    }
    public Task QuitterCanal(int courseId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupeCanal(courseId));

    /// <summary>Lot 3 : suit un sujet du forum ouvert à l'écran (sujets lisibles par tout utilisateur connecté).</summary>
    public async Task<bool> SuivreSujet(int threadId)
    {
        if (!await _db.ForumThreads.AsNoTracking().AnyAsync(t => t.Id == threadId)) return false;
        await Groups.AddToGroupAsync(Context.ConnectionId, GroupeSujet(threadId));
        return true;
    }
    public Task QuitterSujet(int threadId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupeSujet(threadId));

    /// <summary>« En train d'écrire » dans un groupe : relayé aux autres membres.</summary>
    public async Task EcritGroupe(int groupeId)
    {
        var me = Context.User!.GetUserId();
        var membres = await _db.ChatGroupMembers.AsNoTracking()
            .Where(m => m.ChatGroupId == groupeId).Select(m => m.UserId).ToListAsync();
        if (!membres.Contains(me)) return;
        var cibles = membres.Where(id => id != me).Select(GroupeUtilisateur).ToList();
        if (cibles.Count > 0)
            await Clients.Groups(cibles).SendAsync("ecrit", new { type = "groupe", groupeId, de = me });
    }
}
