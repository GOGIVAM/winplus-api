using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services.PaymentProviders;

// ============================================================
// MTN MOBILE MONEY SERVICE
// API: MTN MoMo API (https://momodeveloper.mtn.com/)
// ============================================================

public class MtnMomoConfig
{
    public string BaseUrl         { get; set; } = "https://sandbox.momodeveloper.mtn.com";
    public string SubscriptionKey { get; set; } = "";
    public string ApiUser         { get; set; } = "";
    public string ApiKey          { get; set; } = "";
    public string Environment     { get; set; } = "sandbox";
    public string Currency        { get; set; } = "XAF";

    // FIX 1 : CallbackUrl vide par défaut.
    // L'ancienne valeur "https://votresite.com/api/payments/mtn/callback" est une URL
    // fictive — MTN valide qu'elle est joignable et rejette la requête avec 400 si elle
    // ne répond pas. En sandbox, laisse vide. En production, mets ton URL HTTPS réelle.
    public string CallbackUrl { get; set; } = "";
}

public class MtnMomoService
{
    private readonly HttpClient             _http;
    private readonly MtnMomoConfig          _config;
    private readonly ILogger<MtnMomoService> _logger;
    private string?  _accessToken;

    // FIX 2 : mémoriser l'expiry du token pour ne pas re-authentifier à chaque appel.
    // Dans le code original, _accessToken était vérifié par null uniquement, mais il
    // expire après 1h côté MTN → les appels après expiry retournaient 401 silencieusement.
    private DateTime _tokenExpiry = DateTime.MinValue;

    public MtnMomoService(HttpClient http, MtnMomoConfig config, ILogger<MtnMomoService> logger)
    {
        _http   = http;
        _config = config;
        _logger = logger;
    }

    public async Task<string> GetAccessTokenAsync()
    {
        // FIX 2 : utiliser le token en cache tant qu'il reste plus de 60s de validité.
        // Avant : on re-demandait un token seulement si null, mais après expiry (1h)
        //         tous les appels échouaient avec 401 sans message d'erreur explicite.
        if (_accessToken != null && DateTime.UtcNow < _tokenExpiry.AddSeconds(-60))
        {
            _logger.LogDebug("[MTN] Token depuis cache, expire dans {Sec}s",
                (int)(_tokenExpiry - DateTime.UtcNow).TotalSeconds);
            return _accessToken;
        }

        try
        {
            var credentials = Convert.ToBase64String(
                Encoding.UTF8.GetBytes($"{_config.ApiUser}:{_config.ApiKey}"));

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/collection/token/");

            request.Headers.Add("Ocp-Apim-Subscription-Key", _config.SubscriptionKey);
            request.Headers.Authorization = new AuthenticationHeaderValue("Basic", credentials);

            // FIX 3 : ajouter un body vide explicite.
            // Sans Content, certaines versions de l'API MTN renvoient 411 Length Required.
            request.Content = new StringContent("", Encoding.UTF8, "application/json");

            var response = await _http.SendAsync(request);

            // FIX 4 : remplacer EnsureSuccessStatusCode() par une vérification manuelle
            // pour logguer le body de l'erreur (EnsureSuccessStatusCode ne loggue rien).
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.LogError("[MTN] ❌ Token HTTP {Code}: {Body}",
                    (int)response.StatusCode, errorBody);
                throw new Exception(
                    $"Authentification MTN échouée (HTTP {(int)response.StatusCode}): {errorBody}");
            }

            var json = await response.Content.ReadAsStringAsync();
            var data = JsonSerializer.Deserialize<JsonElement>(json);
            _accessToken = data.GetProperty("access_token").GetString()!;

            // FIX 2 (suite) : récupérer expires_in depuis la réponse MTN (généralement 3600s)
            var expiresIn = data.TryGetProperty("expires_in", out var expProp)
                ? expProp.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn);

            _logger.LogInformation("[MTN] ✅ Token obtenu avec succès, expire à {Expiry:HH:mm:ss}",
                _tokenExpiry);
            return _accessToken;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MTN] Erreur lors de l'obtention du token");
            throw;
        }
    }

    public async Task<MtnPaymentResult> RequestToPayAsync(MtnPaymentRequest payment)
    {
        try
        {
            // FIX 2 (suite) : appeler GetAccessTokenAsync() à chaque fois — le cache
            // interne gère la durée de vie, donc pas de surcoût. Avant, on vérifiait
            // uniquement `if (string.IsNullOrEmpty(_accessToken))` ce qui laissait
            // passer des tokens expirés.
            await GetAccessTokenAsync();

            // FIX 5 : nettoyer le numéro de téléphone → MSISDN pur sans +, espaces, tirets.
            // Le frontend envoie "237650XXXXXX" (déjà correct), mais cette sanitisation
            // protège contre tout cas inattendu comme "+237 650-12-34-56".
            var phone = SanitizePhone(payment.PhoneNumber);

            var referenceId = Guid.NewGuid().ToString();

            // FIX 6 : montant en string ENTIER pour la devise XAF.
            // payment.Amount.ToString() pouvait produire "27588,00" ou "27588.00" selon
            // la culture serveur — MTN rejette les valeurs avec décimales pour XAF.
            var amountStr = payment.Amount.ToString("F0");

            var body = new
            {
                amount      = amountStr,
                currency    = payment.Currency,   // ← était _config.Currency
                externalId  = payment.OrderId,
                payer = new
                {
                    partyIdType = "MSISDN",
                    partyId     = phone
                },
                payerMessage = Truncate(payment.Description, 160),
                payeeNote    = Truncate($"Commande #{payment.OrderId}", 160),
            };

            _logger.LogInformation(
                "[MTN] RequestToPay — refId: {RefId}, phone: {Phone}, amount: {Amount} {Currency}",
                referenceId, MaskPhone(phone), amountStr, _config.Currency);

            var request = new HttpRequestMessage(HttpMethod.Post,
                $"{_config.BaseUrl}/collection/v1_0/requesttopay");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Headers.Add("X-Reference-Id",            referenceId);
            request.Headers.Add("X-Target-Environment",      _config.Environment);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _config.SubscriptionKey);

            // FIX 1 (suite) : n'envoyer X-Callback-Url que si elle est renseignée.
            // Avant, la valeur fictive était toujours envoyée → MTN la validait → rejet 400.
            if (!string.IsNullOrWhiteSpace(_config.CallbackUrl))
                request.Headers.Add("X-Callback-Url", _config.CallbackUrl);

            request.Content = new StringContent(
                JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

            var response     = await _http.SendAsync(request);
            var responseBody = await response.Content.ReadAsStringAsync();

            _logger.LogInformation("[MTN] RequestToPay — Order: {OrderId}, HTTP: {Code}, Body: {Body}",
                payment.OrderId, (int)response.StatusCode, responseBody);

            // FIX 7 : MTN renvoie 202 Accepted (pas 200) pour un succès.
            // Dans le code original, IsSuccessStatusCode couvrait bien 202, mais si
            // quelqu'un vérifiait StatusCode == 200 ailleurs ça échouait silencieusement.
            // On vérifie maintenant explicitement 202 ET on loggue clairement.
            if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
            {
                _logger.LogInformation("[MTN] ✅ Paiement initié — referenceId: {RefId}", referenceId);
                return new MtnPaymentResult
                {
                    Success     = true,
                    ReferenceId = referenceId,
                    StatusCode  = (int)response.StatusCode,
                    Message     = "Demande de paiement envoyée. En attente de confirmation client.",
                };
            }

            // Erreur — log détaillé pour aider au diagnostic
            LogDiagnosis((int)response.StatusCode, responseBody, phone);

            return new MtnPaymentResult
            {
                Success     = false,
                ReferenceId = referenceId,
                StatusCode  = (int)response.StatusCode,
                Message     = ExtractError(responseBody),
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MTN] Erreur lors de la demande de paiement");
            return new MtnPaymentResult { Success = false, Message = ex.Message };
        }
    }

    public async Task<MtnPaymentStatus> GetPaymentStatusAsync(string referenceId)
    {
        try
        {
            // FIX 2 (suite) : idem — laisser le cache gérer le token
            await GetAccessTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Get,
                $"{_config.BaseUrl}/collection/v1_0/requesttopay/{referenceId}");

            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _accessToken);
            request.Headers.Add("X-Target-Environment",      _config.Environment);
            request.Headers.Add("Ocp-Apim-Subscription-Key", _config.SubscriptionKey);

            var response = await _http.SendAsync(request);
            var json     = await response.Content.ReadAsStringAsync();

            // FIX 8 : gérer les erreurs HTTP avant de désérialiser.
            // Dans le code original, JsonSerializer.Deserialize lancait une exception si
            // l'API renvoyait du HTML ou un body vide (ex : 401, 404) → le catch retournait
            // Status = "ERROR" ce qui interrompait le polling côté frontend.
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("[MTN] Status check HTTP {Code}: {Body}",
                    (int)response.StatusCode, json);
                // FIX 8 : retourner PENDING pour que le frontend continue de poller
                return new MtnPaymentStatus { ReferenceId = referenceId, Status = "PENDING" };
            }

            var data = JsonSerializer.Deserialize<JsonElement>(json);

            return new MtnPaymentStatus
            {
                ReferenceId = referenceId,
                // FIX 9 : ToUpperInvariant() pour normaliser en majuscules.
                // Le frontend compare "SUCCESSFUL", "FAILED", "PENDING" en majuscules.
                // L'API MTN peut renvoyer "successful" ou "Successful" selon les versions.
                Status   = (data.TryGetProperty("status", out var s)
                               ? s.GetString() : "PENDING")?.ToUpperInvariant() ?? "PENDING",
                Amount   = data.TryGetProperty("amount",   out var a) ? a.GetString() : null,
                Currency = data.TryGetProperty("currency", out var c) ? c.GetString() : null,
                // FIX 10 : récupérer le TransactionId financier pour la réconciliation.
                // Utile pour vérifier côté MTN qu'un paiement a bien été capturé.
                TransactionId = data.TryGetProperty("financialTransactionId", out var t)
                                    ? t.GetString() : null,
                Reason   = data.TryGetProperty("reason",   out var r) ? r.GetString() : null,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[MTN] Erreur lors de la vérification du statut");
            // FIX 8 (suite) : PENDING au lieu de ERROR → le polling frontend continue
            return new MtnPaymentStatus { ReferenceId = referenceId, Status = "PENDING" };
        }
    }

    // ── Utilitaires privés ───────────────────────────────────────────────────

    /// <summary>Retire +, espaces, tirets → MSISDN pur. Ex: "+237 650-12-34" → "237650123456"</summary>
    private static string SanitizePhone(string phone)
        => phone.Trim()
                .TrimStart('+')
                .Replace(" ",  "")
                .Replace("-",  "")
                .Replace(".",  "")
                .Replace("(",  "")
                .Replace(")",  "");

    private static string MaskPhone(string p)
        => p.Length > 6 ? p[..3] + "***" + p[^3..] : "***";

    private static string Truncate(string s, int max)
        => s.Length <= max ? s : s[..max];

    private static string ExtractError(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return "Erreur inconnue";
        try
        {
            var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var m)) return m.GetString() ?? body;
            if (doc.RootElement.TryGetProperty("error",   out var e)) return e.GetString() ?? body;
        }
        catch { /* body n'est pas du JSON valide */ }
        return body;
    }

    /// <summary>Logs d'aide au diagnostic selon le code HTTP MTN</summary>
    private void LogDiagnosis(int code, string body, string phone)
    {
        switch (code)
        {
            case 400:
                _logger.LogError(
                    "[MTN DIAG] 400 Bad Request — Vérifiez : " +
                    "① numéro '{Phone}' = MSISDN sans + (ex: 237650XXXXXX), " +
                    "② montant entier pour XAF (pas de décimales), " +
                    "③ currency = 'XAF', " +
                    "④ X-Reference-Id = UUID v4. Body: {Body}", phone, body);
                break;
            case 401:
                _logger.LogError(
                    "[MTN DIAG] 401 Unauthorized — ApiUser, ApiKey ou SubscriptionKey " +
                    "incorrects dans appsettings.json. Body: {Body}", body);
                break;
            case 403:
                _logger.LogError(
                    "[MTN DIAG] 403 Forbidden — En SANDBOX : le numéro '{Phone}' " +
                    "doit être créé dans Portail MTN Developer → Collection → Test Users. " +
                    "Body: {Body}", phone, body);
                break;
            case 404:
                _logger.LogError(
                    "[MTN DIAG] 404 Not Found — BaseUrl incorrecte ? " +
                    "Sandbox = 'https://sandbox.momodeveloper.mtn.com'. Body: {Body}", body);
                break;
            case 409:
                _logger.LogError(
                    "[MTN DIAG] 409 Conflict — X-Reference-Id déjà utilisé. " +
                    "Chaque appel doit avoir un Guid.NewGuid() unique. Body: {Body}", body);
                break;
            default:
                _logger.LogError("[MTN DIAG] HTTP {Code} inattendu. Body: {Body}", code, body);
                break;
        }
    }
}

// ── Modèles ──────────────────────────────────────────────────────────────────

public class MtnPaymentRequest
{
    public string  OrderId     { get; set; } = "";
    public string  PhoneNumber { get; set; } = "";
    public decimal Amount      { get; set; }
    public string  Description { get; set; } = "";
    public string  Currency    { get; set; } = "XAF";   // ← ajouter cette ligne

}

public class MtnPaymentResult
{
    public bool   Success     { get; set; }
    public string ReferenceId { get; set; } = "";
    public int    StatusCode  { get; set; }
    public string Message     { get; set; } = "";
}

public class MtnPaymentStatus
{
    public string  ReferenceId   { get; set; } = "";
    /// <summary>PENDING | SUCCESSFUL | FAILED</summary>
    public string  Status        { get; set; } = "";
    public string? Amount        { get; set; }
    public string? Currency      { get; set; }
    // FIX 10 : champs ajoutés (non présents dans l'original) pour la réconciliation
    public string? TransactionId { get; set; }
    public string? Reason        { get; set; }
}