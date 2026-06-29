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
        _fromName  = configuration["Resend:FromName"]  ?? "WinPlus";

        var apiKey = configuration["Resend:ApiKey"]
            ?? throw new InvalidOperationException("Resend:ApiKey non configuré");

        _httpClient = httpClientFactory.CreateClient("ResendClient");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Vérification du compte
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode)
    {
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Bienvenue sur WinPlus ! Pour activer votre compte et commencer à préparer vos concours,
        entrez le code ci-dessous dans l'application.
      </p>

      <!-- Code block -->
      <div style=""background:#E6FAF9;border:2px solid #33BBAF;border-radius:12px;padding:32px 24px;text-align:center;margin:0 0 24px"">
        <p style=""margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:2px;color:#259A8E;text-transform:uppercase"">Votre code de vérification</p>
        <span style=""font-size:42px;font-weight:700;letter-spacing:12px;color:#0F2A35;font-family:monospace"">{EscapeHtml(verificationCode)}</span>
        <p style=""margin:12px 0 0;font-size:12px;color:#6B8A95"">⏳ Ce code expire dans <strong>24 heures</strong></p>
      </div>

      <p style=""margin:0;font-size:13px;color:#8BA3AC"">
        Si vous n'avez pas créé de compte WinPlus, ignorez simplement cet e-mail.
      </p>";

        var html = Wrapper("Confirmez votre adresse e-mail", body, iconEmoji: "✉️");
        return await SendGenericEmailAsync(email, "Votre code de vérification WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Réinitialisation du mot de passe
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken)
    {
        var resetUrl = $"https://winplus.cm/reset-password?token={Uri.EscapeDataString(resetToken)}";
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Vous avez demandé à réinitialiser votre mot de passe WinPlus.
        Cliquez sur le bouton ci-dessous — ce lien est valable <strong>1 heure</strong>.
      </p>

      <div style=""text-align:center;margin:0 0 24px"">
        <a href=""{resetUrl}""
           style=""display:inline-block;padding:15px 36px;background:#0F2A35;color:#4DD8CC;
                  text-decoration:none;border-radius:10px;font-weight:700;font-size:15px;
                  letter-spacing:0.5px"">
          Réinitialiser mon mot de passe
        </a>
      </div>

      <p style=""margin:0 0 8px;font-size:12px;color:#8BA3AC"">Ou copiez ce lien dans votre navigateur :</p>
      <p style=""margin:0 0 24px;font-size:12px"">
        <a href=""{resetUrl}"" style=""color:#259A8E;word-break:break-all"">{resetUrl}</a>
      </p>

      <div style=""background:#FFF8E6;border-left:4px solid #F59E0B;border-radius:0 8px 8px 0;padding:14px 16px;margin:0 0 16px"">
        <p style=""margin:0;font-size:13px;color:#92400E"">
          <strong>⚠️ Vous n'avez pas fait cette demande ?</strong><br>
          Votre compte est en sécurité. Ignorez simplement cet e-mail.
        </p>
      </div>";

        var html = Wrapper("Réinitialisation de mot de passe", body, iconEmoji: "🔐");
        return await SendGenericEmailAsync(email, "Réinitialisation de votre mot de passe WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Mot de passe modifié
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPasswordChangedAsync(string email, string firstName)
    {
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Le mot de passe de votre compte WinPlus a bien été modifié avec succès.
      </p>

      <div style=""background:#FFF8E6;border-left:4px solid #F59E0B;border-radius:0 8px 8px 0;padding:14px 16px;margin:0 0 24px"">
        <p style=""margin:0;font-size:13px;color:#92400E"">
          <strong>Ce n'était pas vous ?</strong><br>
          Contactez immédiatement notre support pour sécuriser votre compte.
        </p>
      </div>

      <div style=""text-align:center;margin:0 0 16px"">
        <a href=""https://winplus.cm/support""
           style=""display:inline-block;padding:13px 30px;background:#0F2A35;color:#4DD8CC;
                  text-decoration:none;border-radius:10px;font-weight:700;font-size:14px"">
          Contacter le support
        </a>
      </div>";

        var html = Wrapper("Mot de passe modifié", body, iconEmoji: "🔒");
        return await SendGenericEmailAsync(email, "Votre mot de passe WinPlus a été modifié", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Nouvelle connexion détectée
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress)
    {
        var time = DateTime.UtcNow.ToString("dd/MM/yyyy à HH:mm") + " UTC";
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Une connexion a été effectuée depuis un nouvel appareil sur votre compte WinPlus.
      </p>

      <div style=""background:#F7F4EE;border:1px solid #E0D8C8;border-radius:10px;padding:20px 24px;margin:0 0 24px"">
        <table style=""width:100%;border-collapse:collapse"">
          <tr>
            <td style=""padding:8px 0;font-size:13px;color:#6B8A95;border-bottom:1px solid #E0D8C8"">Appareil</td>
            <td style=""padding:8px 0;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #E0D8C8"">{EscapeHtml(deviceName)}</td>
          </tr>
          <tr>
            <td style=""padding:8px 0;font-size:13px;color:#6B8A95;border-bottom:1px solid #E0D8C8"">Adresse IP</td>
            <td style=""padding:8px 0;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #E0D8C8;font-family:monospace"">{EscapeHtml(ipAddress)}</td>
          </tr>
          <tr>
            <td style=""padding:8px 0;font-size:13px;color:#6B8A95"">Date</td>
            <td style=""padding:8px 0;font-size:13px;font-weight:600;color:#0F2A35;text-align:right"">{time}</td>
          </tr>
        </table>
      </div>

      <p style=""margin:0;font-size:13px;color:#8BA3AC"">
        ✅ <strong>C'était vous ?</strong> Aucune action requise.<br>
        ⚠️ <strong>Ce n'était pas vous ?</strong> Changez immédiatement votre mot de passe.
      </p>";

        var html = Wrapper("Nouvelle connexion détectée", body, iconEmoji: "📱");
        return await SendGenericEmailAsync(email, "Nouvelle connexion sur votre compte WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Code 2FA
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code)
    {
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Votre code de double authentification WinPlus. Saisissez-le pour finaliser votre connexion.
      </p>

      <div style=""background:#E6FAF9;border:2px solid #33BBAF;border-radius:12px;padding:32px 24px;text-align:center;margin:0 0 24px"">
        <p style=""margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:2px;color:#259A8E;text-transform:uppercase"">Code 2FA</p>
        <span style=""font-size:42px;font-weight:700;letter-spacing:12px;color:#0F2A35;font-family:monospace"">{EscapeHtml(code)}</span>
        <p style=""margin:12px 0 0;font-size:12px;color:#6B8A95"">⏳ Expire dans <strong>5 minutes</strong></p>
      </div>

      <p style=""margin:0;font-size:13px;color:#8BA3AC"">
        Si vous n'avez pas tenté de vous connecter, ignorez cet e-mail.
      </p>";

        var html = Wrapper("Code d'authentification", body, iconEmoji: "🔑");
        return await SendGenericEmailAsync(email, "Votre code 2FA WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Vérification changement d'e-mail
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode)
    {
        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Vous avez demandé à changer votre adresse e-mail. Entrez le code ci-dessous
        pour confirmer votre nouvelle adresse.
      </p>

      <div style=""background:#E6FAF9;border:2px solid #33BBAF;border-radius:12px;padding:32px 24px;text-align:center;margin:0 0 24px"">
        <p style=""margin:0 0 8px;font-size:12px;font-weight:600;letter-spacing:2px;color:#259A8E;text-transform:uppercase"">Code de confirmation</p>
        <span style=""font-size:42px;font-weight:700;letter-spacing:12px;color:#0F2A35;font-family:monospace"">{EscapeHtml(verificationCode)}</span>
        <p style=""margin:12px 0 0;font-size:12px;color:#6B8A95"">⏳ Ce code expire dans <strong>24 heures</strong></p>
      </div>

      <p style=""margin:0;font-size:13px;color:#8BA3AC"">
        Si vous n'avez pas fait cette demande, ignorez cet e-mail — votre adresse actuelle reste inchangée.
      </p>";

        var html = Wrapper("Vérification de votre nouvel e-mail", body, iconEmoji: "✉️");
        return await SendGenericEmailAsync(email, "Vérification de votre nouvel e-mail WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Confirmation de paiement
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPaymentConfirmationAsync(string email, string firstName, decimal amount, string reference, DateTime completedAt)
    {
        var formattedAmount = amount.ToString("N0").Replace(",", " ");
        var formattedDate   = completedAt.ToString("dd/MM/yyyy à HH:mm") + " UTC";

        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Votre paiement a bien été reçu et validé. Vos cours sont maintenant accessibles.
      </p>

      <!-- Reçu -->
      <div style=""background:#E6FAF9;border:1px solid #33BBAF;border-radius:12px;padding:24px;margin:0 0 24px"">
        <p style=""margin:0 0 16px;font-size:12px;font-weight:700;letter-spacing:2px;color:#259A8E;text-transform:uppercase"">Reçu de paiement</p>
        <table style=""width:100%;border-collapse:collapse"">
          <tr>
            <td style=""padding:10px 0;font-size:13px;color:#6B8A95;border-bottom:1px solid #B2E8E3"">Référence</td>
            <td style=""padding:10px 0;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;font-family:monospace;border-bottom:1px solid #B2E8E3"">{EscapeHtml(reference)}</td>
          </tr>
          <tr>
            <td style=""padding:10px 0;font-size:13px;color:#6B8A95;border-bottom:1px solid #B2E8E3"">Date</td>
            <td style=""padding:10px 0;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #B2E8E3"">{formattedDate}</td>
          </tr>
          <tr>
            <td style=""padding:10px 0;font-size:13px;color:#6B8A95;border-bottom:1px solid #B2E8E3"">Statut</td>
            <td style=""padding:10px 0;text-align:right;border-bottom:1px solid #B2E8E3"">
              <span style=""display:inline-block;background:#0F2A35;color:#4DD8CC;padding:3px 10px;border-radius:99px;font-size:11px;font-weight:700"">✓ Confirmé</span>
            </td>
          </tr>
          <tr>
            <td style=""padding:14px 0 4px;font-size:13px;color:#6B8A95"">Montant</td>
            <td style=""padding:14px 0 4px;text-align:right;font-size:26px;font-weight:700;color:#0F2A35"">{formattedAmount} <span style=""font-size:14px;color:#259A8E"">XAF</span></td>
          </tr>
        </table>
      </div>

      <div style=""text-align:center;margin:0 0 16px"">
        <a href=""https://winplus.cm/dashboard""
           style=""display:inline-block;padding:15px 36px;background:#0F2A35;color:#4DD8CC;
                  text-decoration:none;border-radius:10px;font-weight:700;font-size:15px"">
          Accéder à mes cours →
        </a>
      </div>";

        var html = Wrapper("Paiement confirmé", body, iconEmoji: "✅", accentGreen: true);
        return await SendGenericEmailAsync(email, $"Reçu de paiement — {formattedAmount} XAF", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Rappel expiration abonnement
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendSubscriptionExpiryReminderAsync(string email, string firstName, DateTime expiryDate)
    {
        var formattedDate = expiryDate.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        var body = $@"
      <p style=""margin:0 0 16px"">Bonjour <strong>{EscapeHtml(firstName)}</strong>,</p>
      <p style=""margin:0 0 24px;color:#4A6670"">
        Votre abonnement WinPlus arrive à expiration dans <strong>3 jours</strong>, le <strong>{formattedDate}</strong>.
        Renouvelez-le pour continuer à accéder à vos cours sans interruption.
      </p>

      <div style=""background:#FFF8E6;border:1px solid #FDE68A;border-radius:10px;padding:20px 24px;margin:0 0 24px"">
        <p style=""margin:0;font-size:13px;color:#92400E"">
          ⏳ <strong>Plus que 3 jours</strong> pour renouveler avant la suspension de votre accès.
        </p>
      </div>

      <div style=""text-align:center;margin:0 0 24px"">
        <a href=""https://winplus.cm/pricing""
           style=""display:inline-block;padding:15px 36px;background:#0F2A35;color:#4DD8CC;
                  text-decoration:none;border-radius:10px;font-weight:700;font-size:15px"">
          Renouveler mon abonnement →
        </a>
      </div>

      <p style=""margin:0;font-size:13px;color:#8BA3AC"">
        Besoin d'aide ? <a href=""https://winplus.cm/support"" style=""color:#259A8E;font-weight:600"">Contactez notre support</a>.
      </p>";

        var html = Wrapper("Votre abonnement expire bientôt", body, iconEmoji: "⏳");
        return await SendGenericEmailAsync(email, "Votre abonnement WinPlus expire dans 3 jours", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  ENVOI GÉNÉRIQUE
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendGenericEmailAsync(string to, string subject, string htmlContent)
    {
        try
        {
            var payload = new
            {
                from    = $"{_fromName} <{_fromEmail}>",
                to      = new[] { to },
                subject,
                html    = htmlContent
            };

            var json    = JsonSerializer.Serialize(payload, _json);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync("https://api.resend.com/emails", content);
            var body     = await response.Content.ReadAsStringAsync();

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

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS PRIVÉS
    // ─────────────────────────────────────────────────────────────────────────

    private static string EscapeHtml(string? value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>
    /// Enveloppe chaque e-mail dans le layout branded WinPlus :
    ///  – header avec logo SVG inline + couleurs Ink/Teal
    ///  – corps dans une carte blanche centrée
    ///  – footer avec liens légaux
    /// </summary>
    private static string Wrapper(string title, string bodyHtml, string iconEmoji = "📬", bool accentGreen = false)
    {
        // Logo SVG inline (Win+ stylisé, palette Ink + Teal)
        const string LogoSvg = @"
<svg width=""110"" height=""32"" viewBox=""0 0 110 32"" fill=""none"" xmlns=""http://www.w3.org/2000/svg"">
  <!-- Icône carré arrondi -->
  <rect width=""32"" height=""32"" rx=""8"" fill=""#4DD8CC""/>
  <text x=""16"" y=""22"" font-family=""Georgia,serif"" font-size=""16"" font-weight=""700""
        fill=""#0F2A35"" text-anchor=""middle"">W+</text>
  <!-- Texte WinPlus -->
  <text x=""40"" y=""12"" font-family=""Georgia,serif"" font-size=""13"" font-weight=""700""
        fill=""#0F2A35"">Win</text>
  <text x=""65"" y=""12"" font-family=""Georgia,serif"" font-size=""13"" font-weight=""700""
        fill=""#33BBAF"">Plus</text>
  <text x=""40"" y=""26"" font-family=""Arial,sans-serif"" font-size=""8"" letter-spacing=""1.5""
        fill=""#8BA3AC"">RÉUSSITE · CAMEROUN</text>
</svg>";

        var headerBg = accentGreen ? "#259A8E" : "#0F2A35";
        var headerTextColor = "#4DD8CC";

        return $@"<!DOCTYPE html>
<html lang=""fr"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width,initial-scale=1"">
<title>{EscapeHtml(title)}</title>
</head>
<body style=""margin:0;padding:0;background:#F0EBE0;font-family:'Helvetica Neue',Arial,sans-serif"">

  <!-- Préheader invisible -->
  <div style=""display:none;max-height:0;overflow:hidden;mso-hide:all"">
    {EscapeHtml(title)} — WinPlus ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌ ‌
  </div>

  <!-- Wrapper centré -->
  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
    <tr>
      <td align=""center"" style=""padding:40px 16px"">
        <table role=""presentation"" width=""100%"" style=""max-width:600px"" cellpadding=""0"" cellspacing=""0"" border=""0"">

          <!-- ── HEADER ── -->
          <tr>
            <td style=""background:{headerBg};border-radius:16px 16px 0 0;padding:28px 36px"">
              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                <tr>
                  <td>{LogoSvg}</td>
                  <td align=""right"" style=""font-size:24px"">{iconEmoji}</td>
                </tr>
              </table>
              <p style=""margin:20px 0 0;font-size:22px;font-weight:700;color:{headerTextColor};
                         font-family:Georgia,serif;letter-spacing:-0.3px"">
                {EscapeHtml(title)}
              </p>
            </td>
          </tr>

          <!-- ── CORPS ── -->
          <tr>
            <td style=""background:#FFFFFF;padding:36px 36px 28px;border-left:1px solid #E0D8C8;border-right:1px solid #E0D8C8"">
              <div style=""font-size:15px;line-height:1.7;color:#1A3845"">
                {bodyHtml}
              </div>
            </td>
          </tr>

          <!-- ── FOOTER ── -->
          <tr>
            <td style=""background:#F7F4EE;border:1px solid #E0D8C8;border-top:none;
                        border-radius:0 0 16px 16px;padding:20px 36px"">
              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                <tr>
                  <td style=""font-size:12px;color:#8BA3AC;line-height:1.6"">
                    Cet e-mail vous a été envoyé par <strong style=""color:#4A6670"">WinPlus</strong>,
                    la plateforme de préparation aux concours au Cameroun.<br>
                    <a href=""https://winplus.cm"" style=""color:#259A8E;text-decoration:none"">winplus.cm</a>
                    &nbsp;·&nbsp;
                    <a href=""https://winplus.cm/privacy"" style=""color:#259A8E;text-decoration:none"">Confidentialité</a>
                    &nbsp;·&nbsp;
                    <a href=""https://winplus.cm/support"" style=""color:#259A8E;text-decoration:none"">Support</a>
                  </td>
                  <td align=""right"" style=""font-size:11px;color:#B0C4CB;white-space:nowrap;vertical-align:top"">
                    © 2025 WinPlus
                  </td>
                </tr>
              </table>
            </td>
          </tr>

        </table>
      </td>
    </tr>
  </table>

</body>
</html>";
    }
}