using Microsoft.AspNetCore.Mvc;

namespace Backend.Services.Entitlements;

/// <summary>Résultat de l'ouverture d'une fonction IA.</summary>
public sealed class AiFeatureTicket
{
    public required int UserId { get; init; }
    public required string FeatureKey { get; init; }
    public AiQuotaDecision Decision { get; init; }
    /// <summary>Null si autorisé ; sinon la réponse à renvoyer telle quelle (403, 402 ou 503).</summary>
    public IActionResult? Refusal { get; init; }
    public bool Allowed => Refusal == null;
}

/// <summary>
/// Lot 9.7b (18.17) : les fonctions IA hors chat sont <b>activables par plan</b>
/// (droit <c>ai.feature.*</c>) et prélevées sur <b>le même budget de tokens que
/// le chat</b> (fenêtres session et semaine, 8.10). Usage dans un contrôleur :
/// <code>
/// var t = await _gate.BeginAsync(userId, EntitlementCatalog.AiStudyPlan);
/// if (!t.Allowed) return t.Refusal!;
/// ... appel Python (IFastApiClient) ...
/// await _gate.CompleteAsync(t, served: status is &gt;= 200 and &lt; 300);
/// </code>
/// Le coût réel vient de l'en-tête <c>X-WinAI-Tokens</c> de FastAPI
/// (<see cref="IFastApiClient.ConsumeWinAiTokens"/>), sinon la réserve
/// forfaitaire est gardée pour une réponse servie, rendue sinon.
/// Exposé au chantier B (syllabus de formation : <c>ai.feature.course_builder</c>).
/// </summary>
public interface IAiFeatureGate
{
    Task<AiFeatureTicket> BeginAsync(int userId, string featureKey, CancellationToken ct = default);
    Task CompleteAsync(AiFeatureTicket ticket, bool served, int estimatedTokens = 0, int observedTokens = 0);

    /// <summary>Tokens annoncés par une réponse FastAPI lue sans IFastApiClient.</summary>
    static int TokensOf(System.Net.Http.HttpResponseMessage response) =>
        response.Headers.TryGetValues(FastApiClient.WinAiTokensHeader, out var v) && int.TryParse(v.FirstOrDefault(), out var n) ? n : 0;
}

public class AiFeatureGate : IAiFeatureGate
{
    private readonly IEntitlementService _entitlements;
    private readonly IAiQuotaService _quota;
    private readonly IFastApiClient _fastApi;

    public AiFeatureGate(IEntitlementService entitlements, IAiQuotaService quota, IFastApiClient fastApi)
    {
        _entitlements = entitlements;
        _quota = quota;
        _fastApi = fastApi;
    }

    /// <summary>Source courte du journal (colonne VARCHAR(20)).</summary>
    public static string SourceFor(string featureKey)
    {
        var name = featureKey.StartsWith("ai.feature.") ? featureKey["ai.feature.".Length..] : featureKey;
        var s = "f:" + name;
        return s.Length <= 20 ? s : s[..20];
    }

    public async Task<AiFeatureTicket> BeginAsync(int userId, string featureKey, CancellationToken ct = default)
    {
        var ent = await _entitlements.GetAsync(userId, ct);
        if (!ent.Has(featureKey))
        {
            var label = EntitlementCatalog.Find(featureKey)?.Label ?? featureKey;
            return new AiFeatureTicket
            {
                UserId = userId,
                FeatureKey = featureKey,
                Refusal = new ObjectResult(new
                {
                    error = $"« {label} » n'est pas inclus dans votre plan. Passez à un plan supérieur pour l'utiliser.",
                    code = "feature_not_in_plan",
                    feature = featureKey,
                    upgradeRequired = true,
                }) { StatusCode = 403 },
            };
        }

        // Réinitialise le compteur de l'en-tête : seuls les appels de CETTE fonction comptent.
        _fastApi.ConsumeWinAiTokens();
        var decision = await _quota.CheckAndReserveAsync(userId, null, SourceFor(featureKey), ct);
        if (decision.Outcome == AiQuotaOutcome.Unavailable)
            return new AiFeatureTicket
            {
                UserId = userId, FeatureKey = featureKey, Decision = decision,
                Refusal = new ObjectResult(new { error = "WinAI est momentanément indisponible. Réessayez dans un instant.", code = "ai_quota_unavailable" }) { StatusCode = 503 },
            };
        if (!decision.Allowed)
            return new AiFeatureTicket
            {
                UserId = userId, FeatureKey = featureKey, Decision = decision,
                Refusal = new ObjectResult(new
                {
                    error = decision.LimitKind is AiLimitKind kind ? AiUsagePolicy.LimitMessage(kind, decision.ResetsAt) : "Limite WinAI atteinte.",
                    code = "ai_quota_exceeded",
                    limit = decision.LimitKind == AiLimitKind.Session ? "session" : "week",
                    resetsAt = decision.ResetsAt,
                }) { StatusCode = 402 },
            };
        return new AiFeatureTicket { UserId = userId, FeatureKey = featureKey, Decision = decision };
    }

    /// <summary>
    /// Clôt la réserve : coût réel annoncé par FastAPI (en-tête
    /// X-WinAI-Tokens, via <see cref="IFastApiClient"/> ou passé par
    /// <paramref name="observedTokens"/> pour un appel HTTP direct) ; à défaut,
    /// estimation fournie pour un flux servi (le middleware ne peut pas
    /// compter un corps encore à venir) ; sinon aucun appel au modèle n'a eu
    /// lieu et la réserve est rendue.
    /// </summary>
    public async Task CompleteAsync(AiFeatureTicket ticket, bool served, int estimatedTokens = 0, int observedTokens = 0)
    {
        if (!ticket.Allowed) return;
        var observed = Math.Max(observedTokens, 0) + _fastApi.ConsumeWinAiTokens();
        if (observed > 0)
            await _quota.FinalizeUsageAsync(ticket.UserId, ticket.Decision.ReservationId, observed, CancellationToken.None);
        else if (served && estimatedTokens > 0)
            await _quota.FinalizeUsageAsync(ticket.UserId, ticket.Decision.ReservationId,
                Math.Max(AiUsagePolicy.ProvisionalTokensPerMessage, estimatedTokens), CancellationToken.None);
        else
            await _quota.ReleaseReservationAsync(ticket.UserId, ticket.Decision.ReservationId, ticket.Decision.AttemptId, CancellationToken.None);
    }
}
