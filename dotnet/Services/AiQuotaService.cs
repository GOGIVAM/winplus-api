using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Backend.Services.Entitlements;

namespace Backend.Services;

/// <summary>
/// Règle unique du quota WinAI <b>Partie 8 du suivi, révisée en 8.10</b>.
///
/// - <b>8.1 Unité</b> : tokens LLM réels (input+output), journalisés dans
///   <see cref="AiTokenUsage"/> (une ligne par message utilisateur).
/// - <b>8.3/8.4</b> : chaque plan a UNE valeur de référence
///   (<c>PricingPlans.MaxChatMessages</c>, repli <see cref="AiUsagePolicy"/>) ;
///   le plan gratuit a une référence non nulle, base du multiplicateur 1x.
/// - <b>8.6</b> : référence totale = plan personnel + bonus parent + bonus
///   établissement, bonus plafonnés globalement (8.10, voir
///   <see cref="AiUsagePolicy.SharedBonus"/>).
/// - <b>8.10</b> : il n'y a PLUS de plafond mensuel appliqué. Deux limites
///   dérivées de la référence totale : session (5 h glissantes) et semaine
///   (7 jours glissants). Refus si l'une OU l'autre est atteinte. Calcul
///   depuis le journal seul (<see cref="AiUsagePolicy.Evaluate"/>).
/// - <b>8.7/8.11</b> : un <c>ClientMessageId</c> n'est rattaché sans nouveau
///   décompte que si sa ligne n'est PAS finalisée et a moins de
///   <see cref="AiUsagePolicy.ReplayWindow"/>. Un identifiant finalisé ou
///   trop ancien est consommé : la requête est décomptée comme un nouveau
///   message.
///
/// Tolérance à l'absence de la table (point C) : la lecture de consommation
/// dégrade proprement (<see cref="AiQuotaSnapshot.UsageUnavailable"/>) et la
/// réservation renvoie <see cref="AiQuotaOutcome.Unavailable"/> au lieu d'une
/// exception. L'ordre de déploiement (script SQL AVANT le code) reste
/// obligatoire, voir <c>Migrations/SQL_SeedPricingPlanTokenQuotas.sql</c>.
/// </summary>
public interface IAiQuotaService
{
    /// <summary>
    /// État du quota d'un utilisateur, toutes sources additionnées (8.6), avec
    /// l'état des deux fenêtres (8.10). Lecture seule. Ne lève pas si la table
    /// du journal manque : renvoie alors un état sans consommation.
    /// </summary>
    Task<AiQuotaSnapshot> GetSnapshotAsync(int userId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mur de quota, à appeler AVANT l'appel au modèle. Pose une réserve pour
    /// ce message, ou rattache la réserve encore ouverte du même message
    /// (repli REST, « Réessayer »). Voir <see cref="AiQuotaOutcome"/>.
    /// </summary>
    Task<AiQuotaDecision> CheckAndReserveAsync(
        int userId,
        string? clientMessageId,
        string source,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Remplace la réserve par le coût réel (8.1). Sans effet si la ligne est
    /// déjà finalisée (le premier coût réel fait foi) ou si
    /// <paramref name="actualTokens"/> ≤ 0 (coût inconnu : c'est à l'appelant
    /// de libérer ou de finaliser sur estimation).
    /// </summary>
    Task FinalizeUsageAsync(
        int userId,
        string reservationId,
        int actualTokens,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Libère une réserve non finalisée quand aucune réponse n'a été servie.
    /// N'agit que si <paramref name="attemptId"/> détient encore la réserve :
    /// un autre chemin qui l'a rattachée entre-temps la garde.
    /// </summary>
    Task ReleaseReservationAsync(
        int userId,
        string reservationId,
        string attemptId,
        CancellationToken cancellationToken = default);
}

/// <summary>Issue d'une tentative de consommation WinAI.</summary>
public enum AiQuotaOutcome
{
    /// <summary>Nouvelle réserve posée.</summary>
    Reserved,
    /// <summary>Réserve ouverte du même message rattachée, sans second décompte.</summary>
    Reattached,
    /// <summary>Limite de session ou hebdomadaire atteinte (402).</summary>
    LimitReached,
    /// <summary>Journal indisponible (table absente) : refus propre (503).</summary>
    Unavailable,
}

/// <summary>
/// Résultat du mur de quota. <paramref name="ReservationId"/> = identifiant
/// de ligne retenu (celui du client, ou un identifiant serveur) ;
/// <paramref name="AttemptId"/> = identifiant de CETTE tentative, à repasser à
/// <see cref="IAiQuotaService.ReleaseReservationAsync"/>.
/// </summary>
public readonly record struct AiQuotaDecision(
    AiQuotaOutcome Outcome,
    string ReservationId,
    string AttemptId,
    AiLimitKind? LimitKind,
    DateTime? ResetsAt)
{
    public bool Allowed => Outcome is AiQuotaOutcome.Reserved or AiQuotaOutcome.Reattached;
    public bool AlreadyCharged => Outcome == AiQuotaOutcome.Reattached;
}

/// <summary>
/// Photo du quota. Les grandeurs en tokens sont internes ; seuls
/// <see cref="Multiplier"/>, <see cref="LimitReached"/> et
/// <see cref="ResetsAt"/> sont destinés à l'affichage (8.3).
/// </summary>
public sealed record AiQuotaSnapshot(
    int ReferenceTokens,
    int PersonalTokens,
    int ParentBonusTokens,
    int InstitutionBonusTokens,
    AiWindowState Session,
    AiWindowState Week,
    bool UsageUnavailable,
    long FreeReferenceTokens = 200_000)
{
    public int Multiplier => AiUsagePolicy.MultiplierFor(ReferenceTokens, FreeReferenceTokens);
    public AiLimitKind? LimitReached => AiUsagePolicy.Binding(Session, Week).Kind;
    public DateTime? ResetsAt => AiUsagePolicy.Binding(Session, Week).ResetsAt;
    public bool IsExhausted => LimitReached is not null;
}

public class AiQuotaService : IAiQuotaService
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AiQuotaService> _logger;
    private readonly IEntitlementService _entitlements;

    public AiQuotaService(ApplicationDbContext context, ILogger<AiQuotaService> logger, IEntitlementService entitlements)
    {
        _context = context;
        _logger = logger;
        _entitlements = entitlements;
    }

    /// <summary>Compatibilité : réserve forfaitaire (voir <see cref="AiUsagePolicy"/>).</summary>
    public const int ProvisionalTokensPerMessage = AiUsagePolicy.ProvisionalTokensPerMessage;

    // ─── Détection des erreurs Postgres ──────────────────────────────────

    /// <summary>Table ou colonne absente : script SQL pas encore exécuté.</summary>
    private static bool IsMissingSchema(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException pg
                && (pg.SqlState == PostgresErrorCodes.UndefinedTable
                    || pg.SqlState == PostgresErrorCodes.UndefinedColumn))
                return true;
        return false;
    }

    /// <summary>Violation d'unicité réelle (23505), et rien d'autre.</summary>
    private static bool IsUniqueViolation(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException pg && pg.SqlState == PostgresErrorCodes.UniqueViolation)
                return true;
        return false;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    // ─── Lecture ─────────────────────────────────────────────────────────

    public async Task<AiQuotaSnapshot> GetSnapshotAsync(int userId, CancellationToken cancellationToken = default)
    {
        // Lot 9.7b : référence = droit ai.reference_tokens (plan personnel,
        // ou plancher d'un siège de licence) + bonus des donateurs (parent,
        // licence), tous résolus par EntitlementService. Plus aucune lecture
        // de plan par nom ni par prix ici.
        var ent = await _entitlements.GetAsync(userId, cancellationToken);
        long reference = ent.TotalReferenceTokens;
        int referenceClamped = (int)Math.Min(int.MaxValue, reference);

        var now = DateTime.UtcNow;
        List<AiUsageRecord> records;
        bool unavailable = false;
        try
        {
            // Une seule lecture couvre les deux fenêtres (la semaine contient
            // la session). Servie par l'index (UserId, CreatedAt).
            var weekStart = now - AiUsagePolicy.WeekLength;
            records = (await _context.AiTokenUsages
                    .AsNoTracking()
                    .Where(u => u.UserId == userId && u.CreatedAt > weekStart)
                    .Select(u => new { u.CreatedAt, u.TokensCharged })
                    .ToListAsync(cancellationToken))
                .Select(r => new AiUsageRecord(r.CreatedAt, r.TokensCharged))
                .ToList();
        }
        catch (Exception ex) when (IsMissingSchema(ex))
        {
            // Point C : table (ou colonne) absente script SQL pas encore
            // passé. On ne fait pas tomber GET /subscriptions/me : état par
            // défaut, sans consommation, signalé par UsageUnavailable.
            _logger.LogWarning(
                "Journal WinAI \"AiTokenUsages\" absent ou incomplet : exécuter Migrations/SQL_SeedPricingPlanTokenQuotas.sql. " +
                "État du quota renvoyé sans consommation pour l'utilisateur {UserId}.", userId);
            records = new List<AiUsageRecord>();
            unavailable = true;
        }

        var session = AiUsagePolicy.Evaluate(records, now, AiUsagePolicy.SessionLength, AiUsagePolicy.SessionLimit(reference));
        var week = AiUsagePolicy.Evaluate(records, now, AiUsagePolicy.WeekLength, AiUsagePolicy.WeeklyLimit(reference));

        return new AiQuotaSnapshot(
            ReferenceTokens: referenceClamped,
            PersonalTokens: (int)Math.Min(int.MaxValue, ent.PersonalReferenceTokens),
            ParentBonusTokens: (int)Math.Min(int.MaxValue, ent.ParentBonusTokens),
            InstitutionBonusTokens: (int)Math.Min(int.MaxValue, ent.LicenseBonusTokens),
            FreeReferenceTokens: ent.FreeReferenceTokens,
            Session: session,
            Week: week,
            UsageUnavailable: unavailable);
    }

    // ─── Réserve / rattachement ──────────────────────────────────────────

    public async Task<AiQuotaDecision> CheckAndReserveAsync(
        int userId,
        string? clientMessageId,
        string source,
        CancellationToken cancellationToken = default)
    {
        var attemptId = NewId();
        string? clientId = string.IsNullOrWhiteSpace(clientMessageId) ? null : clientMessageId.Trim();
        if (clientId is { Length: > 64 }) clientId = clientId[..64];

        try
        {
            // Au plus 3 tours : une violation d'unicité (même identifiant
            // inséré à l'instant par un chemin concurrent) fait retenter le
            // rattachement ; à la seconde, on bascule sur un identifiant
            // serveur, décompté normalement.
            for (var round = 0; round < 3; round++)
            {
                var now = DateTime.UtcNow;
                string messageId;

                if (clientId is null)
                {
                    messageId = NewId();
                }
                else
                {
                    var existing = await _context.AiTokenUsages
                        .AsNoTracking()
                        .Where(u => u.UserId == userId && u.ClientMessageId == clientId)
                        .Select(u => new { u.IsFinalized, u.CreatedAt })
                        .FirstOrDefaultAsync(cancellationToken);

                    var verdict = AiUsagePolicy.ClassifyExisting(
                        existing is not null,
                        existing?.IsFinalized ?? false,
                        existing?.CreatedAt ?? default,
                        now);

                    if (verdict == AiReplayVerdict.Reattach)
                    {
                        // Prise de possession atomique, sous les MÊMES
                        // conditions que le classement : si la ligne a été
                        // finalisée ou libérée entre-temps, 0 ligne touchée.
                        var cutoff = now - AiUsagePolicy.ReplayWindow;
                        var taken = await _context.AiTokenUsages
                            .Where(u => u.UserId == userId
                                     && u.ClientMessageId == clientId
                                     && !u.IsFinalized
                                     && u.CreatedAt > cutoff)
                            .ExecuteUpdateAsync(s => s
                                .SetProperty(u => u.AttemptId, attemptId)
                                .SetProperty(u => u.Source, source), cancellationToken);

                        if (taken > 0)
                        {
                            _logger.LogInformation(
                                "Quota WinAI : message {MessageId} rattaché à sa réserve ouverte (utilisateur {UserId}, chemin {Source}) pas de second décompte",
                                clientId, userId, source);
                            return new AiQuotaDecision(AiQuotaOutcome.Reattached, clientId, attemptId, null, null);
                        }
                        continue; // état changé entre lecture et écriture : on reclasse
                    }

                    if (verdict == AiReplayVerdict.ConsumedId)
                    {
                        // Point A : identifiant déjà finalisé ou trop ancien. Il
                        // n'ouvre plus rien ; la requête est un nouveau message.
                        _logger.LogInformation(
                            "Quota WinAI : identifiant {MessageId} déjà consommé (finalisé ou > {Minutes} min) pour l'utilisateur {UserId} décompté comme nouveau message",
                            clientId, AiUsagePolicy.ReplayWindowMinutes, userId);
                        clientId = null;
                        messageId = NewId();
                    }
                    else
                    {
                        messageId = clientId;
                    }
                }

                var snapshot = await GetSnapshotAsync(userId, cancellationToken);
                if (snapshot.UsageUnavailable)
                    return new AiQuotaDecision(AiQuotaOutcome.Unavailable, messageId, attemptId, null, null);

                if (snapshot.IsExhausted)
                    return new AiQuotaDecision(AiQuotaOutcome.LimitReached, messageId, attemptId,
                        snapshot.LimitReached, snapshot.ResetsAt);

                var reservation = new AiTokenUsage
                {
                    UserId = userId,
                    ClientMessageId = messageId,
                    TokensCharged = AiUsagePolicy.ProvisionalTokensPerMessage,
                    IsFinalized = false,
                    Source = source,
                    CreatedAt = now,
                    AttemptId = attemptId,
                    // Lot 9.7b : référence totale au moment de la réserve, lue
                    // par le contrôle Python (ai_quota.py) au lieu de recopier
                    // une grille de plans.
                    ReferenceTokens = snapshot.ReferenceTokens,
                };
                _context.AiTokenUsages.Add(reservation);

                try
                {
                    await _context.SaveChangesAsync(cancellationToken);
                    return new AiQuotaDecision(AiQuotaOutcome.Reserved, messageId, attemptId, null, null);
                }
                catch (DbUpdateException ex) when (IsUniqueViolation(ex))
                {
                    // Seule la violation d'unicité réelle (23505) est traitée
                    // comme « même message inséré par un chemin concurrent ».
                    // Toute autre DbUpdateException remonte.
                    _context.Entry(reservation).State = EntityState.Detached;
                    if (round >= 1) clientId = null;
                }
            }

            // Inatteignable en pratique (3 collisions successives).
            _logger.LogWarning("Quota WinAI : réservation impossible après 3 tentatives pour {UserId}", userId);
            return new AiQuotaDecision(AiQuotaOutcome.Unavailable, clientId ?? string.Empty, attemptId, null, null);
        }
        catch (Exception ex) when (IsMissingSchema(ex))
        {
            _logger.LogError(
                "Réservation WinAI impossible : table \"AiTokenUsages\" absente ou incomplète. " +
                "Exécuter Migrations/SQL_SeedPricingPlanTokenQuotas.sql AVANT le déploiement du code.");
            return new AiQuotaDecision(AiQuotaOutcome.Unavailable, clientId ?? string.Empty, attemptId, null, null);
        }
    }

    // ─── Finalisation / libération ───────────────────────────────────────

    public async Task FinalizeUsageAsync(
        int userId,
        string reservationId,
        int actualTokens,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reservationId) || actualTokens <= 0) return;

        try
        {
            var now = DateTime.UtcNow;
            var updated = await _context.AiTokenUsages
                .Where(u => u.UserId == userId && u.ClientMessageId == reservationId && !u.IsFinalized)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.TokensCharged, actualTokens)
                    .SetProperty(u => u.IsFinalized, true)
                    .SetProperty(u => u.FinalizedAt, now), cancellationToken);
            if (updated > 0) return;

            // Déjà finalisée : le premier coût réel fait foi.
            var exists = await _context.AiTokenUsages.AsNoTracking()
                .AnyAsync(u => u.UserId == userId && u.ClientMessageId == reservationId, cancellationToken);
            if (exists) return;

            // Ligne disparue (libérée par une autre tentative pendant que
            // celle-ci servait réellement une réponse) : on réinscrit le coût
            // plutôt que de laisser une réponse servie gratuitement.
            var row = new AiTokenUsage
            {
                UserId = userId,
                ClientMessageId = reservationId,
                TokensCharged = actualTokens,
                IsFinalized = true,
                Source = "recovered",
                CreatedAt = now,
                FinalizedAt = now,
            };
            _context.AiTokenUsages.Add(row);
            try
            {
                await _context.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException ex) when (IsUniqueViolation(ex))
            {
                _context.Entry(row).State = EntityState.Detached;
            }
        }
        catch (Exception ex)
        {
            // Ne jamais faire échouer une réponse déjà servie pour un
            // problème de comptabilité.
            _logger.LogWarning(ex, "Finalisation du décompte WinAI impossible pour {UserId}/{MessageId}", userId, reservationId);
        }
    }

    public async Task ReleaseReservationAsync(
        int userId,
        string reservationId,
        string attemptId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(reservationId) || string.IsNullOrWhiteSpace(attemptId)) return;
        try
        {
            await _context.AiTokenUsages
                .Where(u => u.UserId == userId
                         && u.ClientMessageId == reservationId
                         && !u.IsFinalized
                         && u.AttemptId == attemptId)
                .ExecuteDeleteAsync(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Libération de réserve WinAI impossible pour {UserId}/{MessageId}", userId, reservationId);
        }
    }
}
