using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Backend.Services;

public interface IEmailService
{
    Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode);
    Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken);
    Task<bool> SendPasswordChangedAsync(string email, string firstName);
    Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress);
    Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code);
    Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode);
    Task<bool> SendPaymentConfirmationAsync(string email, string firstName, decimal amount, string reference, DateTime completedAt);
    Task<bool> SendSubscriptionExpiryReminderAsync(string email, string firstName, DateTime expiryDate);
    Task<bool> SendGenericEmailAsync(string to, string subject, string htmlContent);
}

public class EmailService : IEmailService
{
    private readonly HttpClient _httpClient;
    private readonly string _fromEmail;
    private readonly string _fromName;
    private readonly ILogger<EmailService> _logger;

    private static readonly JsonSerializerOptions _json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EmailService(IHttpClientFactory httpClientFactory, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _logger = logger;
        _fromEmail = configuration["Resend:FromEmail"] ?? "support@winplus.cm";
        _fromName = configuration["Resend:FromName"] ?? "WinPlus";

        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey non configuré");

        _httpClient = httpClientFactory.CreateClient("ResendClient");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode)
    {
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Bienvenue sur WinPlus !</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Pour activer votre compte, entrez le code ci-dessous dans l'application :</p>
  <div style='background:#f5f3ff;border:2px solid #7c3aed;border-radius:8px;padding:24px;text-align:center;margin:24px 0'>
    <span style='font-size:36px;font-weight:700;letter-spacing:8px;color:#7c3aed'>{EscapeHtml(verificationCode)}</span>
    <p style='color:#6b7280;margin-top:12px;font-size:13px'>Ce code expire dans 24 heures</p>
  </div>
  <p style='color:#6b7280;font-size:13px'>Si vous n'avez pas créé de compte sur WinPlus, ignorez cet email.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Votre code de vérification WinPlus", html);
    }

    public async Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken)
    {
        var resetUrl = $"https://winplus.cm/reset-password?token={Uri.EscapeDataString(resetToken)}";
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Réinitialisation de mot de passe</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Vous avez demandé à réinitialiser votre mot de passe WinPlus. Cliquez sur le bouton ci-dessous :</p>
  <div style='text-align:center;margin:32px 0'>
    <a href='{resetUrl}' style='display:inline-block;padding:14px 32px;background:#7c3aed;color:#fff;text-decoration:none;border-radius:8px;font-weight:600;font-size:15px'>Réinitialiser mon mot de passe</a>
  </div>
  <p style='color:#6b7280;font-size:13px'>Ou copiez ce lien dans votre navigateur :<br><a href='{resetUrl}' style='color:#7c3aed;word-break:break-all'>{resetUrl}</a></p>
  <p style='color:#dc2626;font-weight:600;margin-top:16px'>⏰ Ce lien expire dans 1 heure.</p>
  <p style='color:#6b7280;font-size:13px'>Si vous n'avez pas fait cette demande, ignorez cet email — votre compte est en sécurité.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Réinitialisation de votre mot de passe WinPlus", html);
    }

    public async Task<bool> SendPasswordChangedAsync(string email, string firstName)
    {
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Mot de passe modifié</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Le mot de passe de votre compte WinPlus a bien été modifié.</p>
  <div style='background:#fef3c7;border-left:4px solid #f59e0b;padding:16px;border-radius:4px;margin:24px 0'>
    <p style='margin:0;color:#92400e'>Si vous n'êtes pas à l'origine de cette modification, contactez immédiatement notre support.</p>
  </div>
  <p style='text-align:center'>
    <a href='https://winplus.cm/support' style='color:#7c3aed;font-weight:600'>Contacter le support</a>
  </p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Votre mot de passe WinPlus a été modifié", html);
    }

    public async Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress)
    {
        var time = DateTime.UtcNow.ToString("dd/MM/yyyy à HH:mm") + " UTC";
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Nouvelle connexion détectée</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Une connexion a été effectuée depuis un nouvel appareil sur votre compte WinPlus :</p>
  <div style='background:#f9fafb;border:1px solid #e5e7eb;border-radius:8px;padding:20px;margin:24px 0'>
    <p style='margin:6px 0'><strong>Appareil :</strong> {EscapeHtml(deviceName)}</p>
    <p style='margin:6px 0'><strong>Adresse IP :</strong> {EscapeHtml(ipAddress)}</p>
    <p style='margin:6px 0'><strong>Date :</strong> {time}</p>
  </div>
  <p style='color:#6b7280;font-size:13px'>C'était vous ? Aucune action requise.<br>Ce n'était pas vous ? Changez immédiatement votre mot de passe.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Nouvelle connexion sur votre compte WinPlus", html);
    }

    public async Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code)
    {
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Code d'authentification</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Votre code de double authentification WinPlus :</p>
  <div style='background:#f5f3ff;border:2px solid #7c3aed;border-radius:8px;padding:24px;text-align:center;margin:24px 0'>
    <span style='font-size:36px;font-weight:700;letter-spacing:8px;color:#7c3aed'>{EscapeHtml(code)}</span>
    <p style='color:#6b7280;margin-top:12px;font-size:13px'>Expire dans 5 minutes</p>
  </div>
  <p style='color:#6b7280;font-size:13px'>Si vous n'avez pas tenté de vous connecter, ignorez cet email.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Votre code 2FA WinPlus", html);
    }

    public async Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode)
    {
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#7c3aed;margin-bottom:8px'>Vérification de votre nouvel email</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Vous avez demandé à changer votre adresse email. Entrez ce code pour confirmer la nouvelle adresse :</p>
  <div style='background:#f5f3ff;border:2px solid #7c3aed;border-radius:8px;padding:24px;text-align:center;margin:24px 0'>
    <span style='font-size:36px;font-weight:700;letter-spacing:8px;color:#7c3aed'>{EscapeHtml(verificationCode)}</span>
    <p style='color:#6b7280;margin-top:12px;font-size:13px'>Ce code expire dans 24 heures</p>
  </div>
  <p style='color:#6b7280;font-size:13px'>Si vous n'avez pas fait cette demande, ignorez cet email.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Vérification de votre nouvel email WinPlus", html);
    }

    public async Task<bool> SendPaymentConfirmationAsync(string email, string firstName, decimal amount, string reference, DateTime completedAt)
    {
        var formattedAmount = amount.ToString("N0").Replace(",", " ");
        var formattedDate = completedAt.ToString("dd/MM/yyyy à HH:mm") + " UTC";
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#059669;margin-bottom:8px'>✅ Paiement confirmé</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Votre paiement a bien été reçu. Voici votre reçu :</p>
  <div style='background:#f0fdf4;border:1px solid #bbf7d0;border-radius:8px;padding:24px;margin:24px 0'>
    <table style='width:100%;border-collapse:collapse'>
      <tr>
        <td style='padding:8px 0;color:#6b7280'>Référence</td>
        <td style='padding:8px 0;text-align:right;font-family:monospace;font-weight:600'>{EscapeHtml(reference)}</td>
      </tr>
      <tr style='border-top:1px solid #d1fae5'>
        <td style='padding:8px 0;color:#6b7280'>Montant</td>
        <td style='padding:8px 0;text-align:right;font-weight:700;font-size:20px;color:#059669'>{formattedAmount} XAF</td>
      </tr>
      <tr style='border-top:1px solid #d1fae5'>
        <td style='padding:8px 0;color:#6b7280'>Date</td>
        <td style='padding:8px 0;text-align:right'>{formattedDate}</td>
      </tr>
      <tr style='border-top:1px solid #d1fae5'>
        <td style='padding:8px 0;color:#6b7280'>Statut</td>
        <td style='padding:8px 0;text-align:right;color:#059669;font-weight:600'>Confirmé</td>
      </tr>
    </table>
  </div>
  <p>Vos cours sont maintenant accessibles depuis votre espace WinPlus.</p>
  <div style='text-align:center;margin:32px 0'>
    <a href='https://winplus.cm/dashboard' style='display:inline-block;padding:14px 32px;background:#7c3aed;color:#fff;text-decoration:none;border-radius:8px;font-weight:600'>Accéder à mes cours</a>
  </div>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, $"Reçu de paiement — {formattedAmount} XAF", html);
    }

    public async Task<bool> SendSubscriptionExpiryReminderAsync(string email, string firstName, DateTime expiryDate)
    {
        var formattedDate = expiryDate.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));
        var html = $@"
<div style='font-family:Arial,sans-serif;max-width:600px;margin:0 auto;padding:24px;color:#1a1a1a'>
  <h2 style='color:#d97706;margin-bottom:8px'>⏳ Votre abonnement expire bientôt</h2>
  <p>Bonjour {EscapeHtml(firstName)},</p>
  <p>Votre abonnement WinPlus arrive à expiration dans <strong>3 jours</strong>, le <strong>{formattedDate}</strong>.</p>
  <div style='background:#fffbeb;border:1px solid #fde68a;border-radius:8px;padding:20px;margin:24px 0'>
    <p style='margin:0;color:#92400e'>Pour continuer à accéder à vos cours et ressources sans interruption, renouvelez votre abonnement avant cette date.</p>
  </div>
  <div style='text-align:center;margin:32px 0'>
    <a href='https://winplus.cm/pricing' style='display:inline-block;padding:14px 32px;background:#7c3aed;color:#fff;text-decoration:none;border-radius:8px;font-weight:600'>Renouveler mon abonnement</a>
  </div>
  <p style='color:#6b7280;font-size:13px'>Besoin d'aide ? <a href='https://winplus.cm/support' style='color:#7c3aed'>Contactez notre support</a>.</p>
  {Footer()}
</div>";

        return await SendGenericEmailAsync(email, "Votre abonnement WinPlus expire dans 3 jours", html);
    }

    public async Task<bool> SendGenericEmailAsync(string to, string subject, string htmlContent)
    {
        try
        {
            var payload = new
            {
                from = $"{_fromName} <{_fromEmail}>",
                to = new[] { to },
                subject,
                html = htmlContent
            };

            var json = JsonSerializer.Serialize(payload, _json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.resend.com/emails", content);
            var body = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                _logger.LogInformation("Email envoyé via Resend à {Email} (sujet: {Subject})", to, subject);
                return true;
            }

            _logger.LogError("Resend a refusé l'email pour {Email}: {Status} — {Body}", to, response.StatusCode, body);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Erreur envoi email Resend vers {Email}", to);
            return false;
        }
    }

    private static string EscapeHtml(string? value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    private static string Footer() => @"
<hr style='border:none;border-top:1px solid #e5e7eb;margin:32px 0'>
<p style='color:#9ca3af;font-size:12px;text-align:center'>
  © 2025 WinPlus · <a href='https://winplus.cm' style='color:#7c3aed;text-decoration:none'>winplus.cm</a>
</p>";
}
