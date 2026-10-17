using Microsoft.AspNetCore.Mvc;

namespace Backend.Services.Entitlements;

/// <summary>
/// Appel FastAPI d'une fonction IA déclenchée par l'utilisateur, passé par
/// <see cref="IAiFeatureGate"/> (lot 9.7b, 18.17) : droit du plan (ou
/// fonction incluse), réserve sur le budget de tokens, envoi, puis débit du
/// coût réel annoncé par l'en-tête X-WinAI-Tokens (réserve rendue si rien
/// n'a été servi). Les traitements déclenchés par le système (tâches de fond,
/// rapports automatiques, alertes, résumés de notifications, affichages
/// automatiques) n'utilisent pas ce chemin : la plateforme les absorbe
/// (étude 9.7b, 1.1).
/// </summary>
public static class AiGateProxy
{
    /// <param name="featureKey">Droit ai.feature.* à exiger ; null pour une fonction incluse dans tous les plans.</param>
    /// <param name="includedSource">Nom court au journal quand <paramref name="featureKey"/> est null.</param>
    public static async Task<(AiFeatureTicket Ticket, HttpResponseMessage? Response)> SendAsync(
        IAiFeatureGate gate, int userId, string? featureKey, string includedSource,
        HttpClient client, HttpRequestMessage request, CancellationToken ct)
    {
        var ticket = featureKey != null
            ? await gate.BeginAsync(userId, featureKey, ct)
            : await gate.BeginIncludedAsync(userId, includedSource, ct);
        if (!ticket.Allowed) return (ticket, null);
        HttpResponseMessage? res = null;
        try
        {
            res = await client.SendAsync(request, ct);
            return (ticket, res);
        }
        finally
        {
            await gate.CompleteAsync(ticket, res?.IsSuccessStatusCode == true,
                observedTokens: res == null ? 0 : IAiFeatureGate.TokensOf(res));
        }
    }

    /// <summary>
    /// Proxy complet : refus du plan ou du budget renvoyé tel quel, sinon le
    /// corps JSON de FastAPI (statut recopié si <paramref name="copyStatus"/>).
    /// </summary>
    public static async Task<IActionResult> ProxyAsync(ControllerBase controller, IAiFeatureGate gate, int userId,
        string? featureKey, string includedSource, HttpClient client, HttpRequestMessage request, CancellationToken ct,
        bool copyStatus = false, Func<string, string>? transform = null)
    {
        var (ticket, res) = await SendAsync(gate, userId, featureKey, includedSource, client, request, ct);
        if (!ticket.Allowed) return ticket.Refusal!;
        using (res)
        {
            var body = await res!.Content.ReadAsStringAsync(ct);
            if (copyStatus) controller.Response.StatusCode = (int)res.StatusCode;
            return controller.Content(transform != null ? transform(body) : body, "application/json");
        }
    }

    /// <summary>Recopie l'en-tête Authorization de la requête entrante (les routes IA de FastAPI exigent un Bearer).</summary>
    public static void ForwardAuthorization(ControllerBase controller, HttpRequestMessage request)
    {
        var auth = controller.HttpContext.Request.Headers["Authorization"].ToString();
        if (!string.IsNullOrEmpty(auth)) request.Headers.TryAddWithoutValidation("Authorization", auth);
    }
}
