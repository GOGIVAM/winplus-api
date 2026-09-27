using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Backend.Models.Entities;

/// <summary>
/// Journal de consommation WinAI, en <b>tokens LLM réels</b> (Partie 8.1 du
/// suivi). Remplace le compteur <c>Subscription.TokensUsedThisMonth</c>, qui
/// ne savait compter que des messages et ne savait porter la consommation que
/// d'un utilisateur ayant lui-même un abonnement.
///
/// Trois raisons d'en faire un journal plutôt qu'un compteur :
///
/// 1. <b>Éligibilité additive (8.6)</b> : un élève peut consommer sans avoir
///    d'abonnement à lui (quota hérité du parent ou de l'établissement). Il
///    n'y a alors aucune ligne <c>Subscriptions</c> où incrémenter quoi que
///    ce soit. Le journal est porté par l'utilisateur, pas par l'abonnement.
///
/// 2. <b>Idempotence par message (8.7)</b> : <see cref="ClientMessageId"/>
///    est l'identifiant généré par le client pour UN message utilisateur.
///    L'index unique (UserId, ClientMessageId) garantit qu'un même message
///    n'est jamais décompté deux fois, quel que soit le nombre de chemins
///    empruntés (flux SSE, repli REST après 8 s, bouton « Réessayer »).
///
/// 3. <b>Coût réel (8.1)</b> : la ligne est d'abord écrite avec une réserve
///    forfaitaire (<see cref="IsFinalized"/> = false) AVANT l'appel au
///    modèle — sans quoi N requêtes parallèles passeraient toutes le mur —
///    puis corrigée au coût réellement facturé par le fournisseur une fois
///    la réponse terminée.
///
/// 4. <b>Fenêtres session / semaine (8.10)</b> : la consommation de chaque
///    fenêtre glissante est la somme de <see cref="TokensCharged"/> sur
///    <see cref="CreatedAt"/> (voir <c>AiUsagePolicy.Evaluate</c>). Aucun
///    compteur séparé : le journal est la seule source.
///
/// ⚠ Ordre de déploiement : la table et la colonne <see cref="AttemptId"/>
/// sont créées par <c>Migrations/SQL_SeedPricingPlanTokenQuotas.sql</c>, à
/// exécuter AVANT de déployer le code qui les lit.
/// </summary>
public class AiTokenUsage
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>
    /// Identifiant du message utilisateur, généré par le client (UUID). Pour
    /// les appelants hérités qui n'en fournissent pas, le serveur en génère un
    /// — la ligne reste alors correcte, seule l'idempotence inter-chemins est
    /// perdue (comportement d'avant la Partie 8).
    /// </summary>
    [Required]
    [MaxLength(64)]
    public string ClientMessageId { get; set; } = string.Empty;

    /// <summary>
    /// Tokens décomptés. Tant que <see cref="IsFinalized"/> est faux, c'est la
    /// réserve forfaitaire ; ensuite, le total réel input+output remonté par
    /// le fournisseur (ou, à défaut, l'estimation à ~4 caractères par token).
    /// </summary>
    public int TokensCharged { get; set; }

    /// <summary>
    /// Faux tant que le coût réel n'a pas remplacé la réserve. Empêche aussi
    /// une double finalisation (le flux SSE et le repli REST peuvent tous deux
    /// se terminer pour le même message).
    /// </summary>
    public bool IsFinalized { get; set; } = false;

    /// <summary>Chemin d'appel : "stream" | "rest" | "python".</summary>
    [MaxLength(20)]
    public string Source { get; set; } = "stream";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime? FinalizedAt { get; set; }

    /// <summary>
    /// Tentative qui « détient » la réserve (point D de la passe 8.10). Chaque
    /// chemin (flux SSE, repli REST, « Réessayer ») qui pose ou rattache la
    /// réserve y inscrit un identifiant neuf ; une libération n'est appliquée
    /// que si elle vient de la tentative détentrice. Sans cela, le flux SSE
    /// abandonné au bout de 8 s pourrait supprimer la réserve que le repli
    /// REST du même message vient de rattacher, et ce repli serait servi
    /// gratuitement. Null pour les lignes antérieures à cette passe.
    /// </summary>
    [MaxLength(32)]
    public string? AttemptId { get; set; }

    [ForeignKey(nameof(UserId))]
    public User? User { get; set; }
}
