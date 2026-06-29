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
    //  EMAIL : Vérification du compte (code à 6 chiffres)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendEmailVerificationAsync(string email, string firstName, string verificationCode)
    {
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 8px 32px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Salut <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, voici ton code à 6 chiffres pour activer ton compte WinPlus. Saisis-le dans l'application pour continuer.
          </p>
        </td></tr>
      </table>

      {OtpBlock(verificationCode)}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 32px;"">
          <span style=""font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">
            Ce code expire dans <span style=""font-family:'SFMono-Regular',Consolas,Menlo,monospace;font-weight:600;color:#1F4A5A;"">24 heures</span>.
          </span>
        </td></tr>
      </table>

      {Divider()}

      {InfoBox("Tu n'as pas créé de compte WinPlus&nbsp;? Tu peux ignorer cet e-mail en toute sécurité.")}";

        var html = Wrapper("Vérification d'email", "Vérifie ton adresse email", body);
        return await SendGenericEmailAsync(email, "Ton code de vérification WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Réinitialisation du mot de passe
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPasswordResetAsync(string email, string firstName, string resetToken)
    {
        var resetUrl = $"https://winplus.cm/reset-password?token={Uri.EscapeDataString(resetToken)}";
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 32px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, vous avez demandé à réinitialiser votre mot de passe WinPlus. Le lien ci-dessous est valable <strong style=""color:#1F4A5A;"">1&nbsp;heure</strong>.
          </p>
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 18px;"">
          {Button("Réinitialiser mon mot de passe", resetUrl)}
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 32px;"">
          <p style=""margin:0 0 6px;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:12px;color:#97AAB2;text-align:center;"">Ou copiez ce lien dans votre navigateur&nbsp;:</p>
          <p style=""margin:0;font-size:12px;text-align:center;word-break:break-all;""><a href=""{resetUrl}"" style=""color:#3471A0;"">{resetUrl}</a></p>
        </td></tr>
      </table>

      {Divider()}

      {WarningBox("Vous n'avez pas fait cette demande&nbsp;?", "Votre compte reste en sécurité — ignorez simplement cet e-mail.")}";

        var html = Wrapper("Sécurité du compte", "Réinitialise ton mot de passe", body);
        return await SendGenericEmailAsync(email, "Réinitialisation de votre mot de passe WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Mot de passe modifié
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPasswordChangedAsync(string email, string firstName)
    {
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 28px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, le mot de passe de votre compte WinPlus a bien été modifié avec succès.
          </p>
        </td></tr>
      </table>

      {WarningBox("Ce n'était pas vous&nbsp;?", "Contactez immédiatement notre support pour sécuriser votre compte.")}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:24px 0 0;"">
          {Button("Contacter le support", "https://winplus.cm/support")}
        </td></tr>
      </table>";

        var html = Wrapper("Sécurité du compte", "Mot de passe modifié", body);
        return await SendGenericEmailAsync(email, "Votre mot de passe WinPlus a été modifié", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Nouvelle connexion détectée
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendNewDeviceLoginAsync(string email, string firstName, string deviceName, string ipAddress)
    {
        var time = DateTime.UtcNow.ToString("dd/MM/yyyy à HH:mm") + " UTC";
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 28px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, une connexion a été effectuée depuis un nouvel appareil sur votre compte WinPlus.
          </p>
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""background-color:#FBF8F2;border:1px solid #E8E0CE;border-radius:14px;padding:8px 22px;margin:0 0 28px;"">
          <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
            <tr>
              <td style=""padding:12px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;border-bottom:1px solid #E8E0CE;"">Appareil</td>
              <td style=""padding:12px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #E8E0CE;"">{EscapeHtml(deviceName)}</td>
            </tr>
            <tr>
              <td style=""padding:12px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;border-bottom:1px solid #E8E0CE;"">Adresse IP</td>
              <td style=""padding:12px 0;font-family:'SFMono-Regular',Consolas,Menlo,monospace;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #E8E0CE;"">{EscapeHtml(ipAddress)}</td>
            </tr>
            <tr>
              <td style=""padding:12px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">Date</td>
              <td style=""padding:12px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;"">{time}</td>
            </tr>
          </table>
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:28px 0 0;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;line-height:1.6;color:#4E7280;text-align:center;"">
            ✅&nbsp;<strong style=""color:#1F4A5A;"">C'était vous&nbsp;?</strong> Aucune action requise.<br>
            ⚠️&nbsp;<strong style=""color:#1F4A5A;"">Ce n'était pas vous&nbsp;?</strong> Changez immédiatement votre mot de passe.
          </p>
        </td></tr>
      </table>";

        var html = Wrapper("Sécurité du compte", "Nouvelle connexion détectée", body);
        return await SendGenericEmailAsync(email, "Nouvelle connexion sur votre compte WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Code 2FA (code à 6 chiffres)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendTwoFactorCodeAsync(string email, string firstName, string code)
    {
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 8px 32px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Salut <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, voici ton code de double authentification. Saisis-le pour finaliser ta connexion.
          </p>
        </td></tr>
      </table>

      {OtpBlock(code)}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 32px;"">
          <span style=""font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">
            Ce code expire dans <span style=""font-family:'SFMono-Regular',Consolas,Menlo,monospace;font-weight:600;color:#1F4A5A;"">5 minutes</span>.
          </span>
        </td></tr>
      </table>

      {Divider()}

      {InfoBox("Tu n'as pas tenté de te connecter&nbsp;? Ignore cet e-mail et vérifie la sécurité de ton compte.")}";

        var html = Wrapper("Double authentification", "Ton code de connexion", body);
        return await SendGenericEmailAsync(email, "Ton code de connexion WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Vérification changement d'e-mail (code à 6 chiffres)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendEmailChangeVerificationAsync(string email, string firstName, string verificationCode)
    {
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 8px 32px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Salut <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, tu as demandé à changer ton adresse e-mail. Voici ton code pour confirmer cette nouvelle adresse.
          </p>
        </td></tr>
      </table>

      {OtpBlock(verificationCode)}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 32px;"">
          <span style=""font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">
            Ce code expire dans <span style=""font-family:'SFMono-Regular',Consolas,Menlo,monospace;font-weight:600;color:#1F4A5A;"">24 heures</span>.
          </span>
        </td></tr>
      </table>

      {Divider()}

      {InfoBox("Tu n'as pas fait cette demande&nbsp;? Ignore cet e-mail — ton adresse actuelle reste inchangée.")}";

        var html = Wrapper("Changement d'email", "Confirme ta nouvelle adresse", body);
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
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 28px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, votre paiement a bien été reçu et validé. Vos cours sont maintenant accessibles.
          </p>
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""background-color:#EBF8F6;border:1px solid #D6F1ED;border-radius:16px;padding:24px;margin:0 0 28px;"">
          <p style=""margin:0 0 16px;font-family:Arial,sans-serif;font-size:12px;font-weight:700;letter-spacing:0.09em;color:#1E8077;text-transform:uppercase;"">Reçu de paiement</p>
          <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
            <tr>
              <td style=""padding:10px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;border-bottom:1px solid #D6F1ED;"">Référence</td>
              <td style=""padding:10px 0;font-family:'SFMono-Regular',Consolas,Menlo,monospace;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #D6F1ED;"">{EscapeHtml(reference)}</td>
            </tr>
            <tr>
              <td style=""padding:10px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;border-bottom:1px solid #D6F1ED;"">Date</td>
              <td style=""padding:10px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;font-weight:600;color:#0F2A35;text-align:right;border-bottom:1px solid #D6F1ED;"">{formattedDate}</td>
            </tr>
            <tr>
              <td style=""padding:10px 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">Statut</td>
              <td style=""padding:10px 0;text-align:right;"">
                <span style=""display:inline-block;background-color:#0F2A35;color:#6BCFC6;padding:4px 12px;border-radius:99px;font-family:Arial,sans-serif;font-size:11px;font-weight:700;"">✓ Confirmé</span>
              </td>
            </tr>
            <tr>
              <td style=""padding:16px 0 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">Montant</td>
              <td style=""padding:16px 0 0;text-align:right;font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;font-size:26px;font-weight:700;color:#0F2A35;"">{formattedAmount} <span style=""font-size:14px;color:#259A8E;"">XAF</span></td>
            </tr>
          </table>
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"">
          {Button("Accéder à mes cours →", "https://winplus.cm/dashboard")}
        </td></tr>
      </table>";

        var html = Wrapper("Paiement", "Paiement confirmé", body, "#1F9D6E");
        return await SendGenericEmailAsync(email, $"Reçu de paiement — {formattedAmount} XAF", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  EMAIL : Rappel expiration abonnement
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendSubscriptionExpiryReminderAsync(string email, string firstName, DateTime expiryDate)
    {
        var formattedDate = expiryDate.ToString("dd MMMM yyyy", new System.Globalization.CultureInfo("fr-FR"));

        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 24px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, votre abonnement WinPlus arrive à expiration dans <strong style=""color:#1F4A5A;"">3 jours</strong>, le <strong style=""color:#1F4A5A;"">{formattedDate}</strong>. Renouvelez-le pour continuer à accéder à vos cours sans interruption.
          </p>
        </td></tr>
      </table>

      {WarningBox("Plus que 3 jours&nbsp;⏳", "Renouvelez avant la suspension de votre accès.")}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:24px 0 28px;"">
          {Button("Renouveler mon abonnement →", "https://winplus.cm/pricing")}
        </td></tr>
      </table>

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#97AAB2;text-align:center;"">
            Besoin d'aide&nbsp;? <a href=""https://winplus.cm/support"" style=""color:#259A8E;font-weight:600;"">Contactez notre support</a>.
          </p>
        </td></tr>
      </table>";

        var html = Wrapper("Abonnement", "Ton abonnement expire bientôt", body);
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
    //  IDENTITÉ VISUELLE — logo WinPlus encodé en base64 (autonome, sans hébergement)
    // ─────────────────────────────────────────────────────────────────────────
    private const string LogoBase64 = "LOGOBASE64PLACEHOLDER";

    // Police d'affichage WinPlus + responsive (petits écrans) — injecté dans le <head>.
    // Constante "plain" (pas d'interpolation) : les accolades CSS restent littérales, sans échappement.
    private const string HeadStyles = @"<style>
  @import url('https://fonts.googleapis.com/css2?family=Bricolage+Grotesque:wght@500;700&family=Instrument+Serif:ital@1&display=swap');
  html, body { margin:0 !important; padding:0 !important; height:100% !important; width:100% !important; }
  table, td { mso-table-lspace:0pt; mso-table-rspace:0pt; border-collapse:collapse !important; }
  img { border:0; outline:none; text-decoration:none; -ms-interpolation-mode:bicubic; display:block; }
  a { text-decoration:none; }
  body { -webkit-font-smoothing:antialiased; }

  @media only screen and (max-width:600px) {
    .email-container { width:100% !important; }
    .stack-px        { padding-left:18px !important; padding-right:18px !important; }
    .card-padding    { padding:36px 24px 30px !important; }
    .otp-cell        { width:40px !important; height:50px !important; font-size:22px !important; }
    .otp-spacer      { width:7px !important; }
    .headline        { font-size:21px !important; }
  }

  @media only screen and (max-width:380px) {
    .card-padding    { padding:30px 16px 26px !important; }
    .otp-cell        { width:32px !important; height:44px !important; font-size:18px !important; }
    .otp-spacer      { width:4px !important; }
    .headline        { font-size:19px !important; }
  }
</style>";

    // ─────────────────────────────────────────────────────────────────────────
    //  HELPERS PRIVÉS — composants visuels réutilisables
    // ─────────────────────────────────────────────────────────────────────────

    private static string EscapeHtml(string? value)
        => System.Net.WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Construit les cases de code à 6 chiffres (calque exact de l'UI produit VerifyCode).</summary>
    private static string OtpBlock(string code)
    {
        var safeCode = EscapeHtml(code ?? string.Empty);
        var sb = new StringBuilder();

        for (int i = 0; i < safeCode.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(@"<td class=""otp-spacer"" style=""width:9px;font-size:1px;"">&nbsp;</td>");
            }

            sb.Append($@"<td class=""otp-cell"" align=""center"" valign=""middle"" style=""width:48px;height:56px;border:1px solid #C8D2D6;border-radius:10px;background-color:#FFFFFF;"">
                  <span style=""font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;font-size:28px;font-weight:500;color:#0F2A35;"">{safeCode[i]}</span>
                </td>");
        }

        return $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 24px;"">
          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr>{sb}</tr></table>
        </td></tr>
      </table>";
    }

    /// <summary>Bouton d'action principal (ink-900 / texte teal clair), identique au .btn-primary de l'app.</summary>
    private static string Button(string label, string url)
        => $@"<a href=""{url}""
         style=""display:inline-block;padding:15px 36px;background-color:#0F2A35;color:#6BCFC6;
                font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;
                text-decoration:none;border-radius:10px;font-weight:700;font-size:15px;letter-spacing:0.01em;"">
        {EscapeHtml(label)}
      </a>";

    /// <summary>Trait de séparation discret (border tokens de l'app).</summary>
    private static string Divider()
        => @"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""border-top:1px solid #E8E0CE;padding:0 0 24px;font-size:1px;line-height:1px;"">&nbsp;</td></tr>
      </table>";

    /// <summary>Encart informatif neutre (teal-50 / teal-100) — pour les notices "ce n'était pas toi".</summary>
    private static string InfoBox(string message)
        => $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""background-color:#EBF8F6;border:1px solid #D6F1ED;border-radius:14px;padding:16px 18px;"">
          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""><tr>
            <td width=""22"" valign=""top"" style=""padding-right:10px;""><span style=""font-family:Arial,sans-serif;font-size:15px;color:#1E8077;"">&#9432;</span></td>
            <td><p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;line-height:1.55;color:#1F4A5A;"">{message}</p></td>
          </tr></table>
        </td></tr>
      </table>";

    /// <summary>Encart d'alerte (gold/ambre) — pour les notices de sécurité plus appuyées.</summary>
    private static string WarningBox(string title, string message)
        => $@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""background-color:#FBF3E3;border:1px solid #EAD9B0;border-radius:14px;padding:16px 18px;"">
          <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"" width=""100%""><tr>
            <td width=""22"" valign=""top"" style=""padding-right:10px;""><span style=""font-family:Arial,sans-serif;font-size:15px;color:#B07A1A;"">&#9888;</span></td>
            <td><p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;line-height:1.55;color:#7A5413;""><strong>{title}</strong><br>{message}</p></td>
          </tr></table>
        </td></tr>
      </table>";

    /// <summary>
    /// Enveloppe chaque e-mail dans le layout WinPlus :
    ///  – logo réel + wordmark "Win+" (Bricolage Grotesque / Instrument Serif)
    ///  – carte blanche arrondie centrée, eyebrow + titre + contenu
    ///  – pied de page avec liens, support@winplus.cm
    ///  – entièrement responsive (petits écrans / mobile)
    /// </summary>
    private static string Wrapper(string eyebrow, string headline, string bodyHtml, string accentColor = "#259A8E")
    {
        return $@"<!DOCTYPE html>
<html lang=""fr"" xmlns=""http://www.w3.org/1999/xhtml"">
<head>
<meta charset=""UTF-8"">
<meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
<meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
<meta name=""color-scheme"" content=""light"">
<meta name=""supported-color-schemes"" content=""light"">
<title>{EscapeHtml(headline)}</title>
<!--[if mso]>
<noscript>
<xml><o:OfficeDocumentSettings><o:PixelsPerInch>96</o:PixelsPerInch><o:AllowPNG/></o:OfficeDocumentSettings></xml>
</noscript>
<![endif]-->
{HeadStyles}
</head>
<body style=""margin:0;padding:0;background-color:#F6F0E4;"">

  <div style=""display:none;max-height:0;overflow:hidden;mso-hide:all;font-size:1px;line-height:1px;color:#F6F0E4;"">
    {EscapeHtml(headline)} — WinPlus&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;&nbsp;&zwnj;
  </div>

  <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""background-color:#F6F0E4;"">
    <tr>
      <td align=""center"" class=""stack-px"" style=""padding:40px 16px;"">

        <table role=""presentation"" width=""600"" cellpadding=""0"" cellspacing=""0"" border=""0"" class=""email-container"" style=""width:600px;max-width:600px;"">

          <!-- Logo + wordmark -->
          <tr>
            <td align=""center"" style=""padding:0 0 28px;"">
              <table role=""presentation"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                <tr>
                  <td style=""padding:0 8px 0 0;vertical-align:middle;"">
                    <img src=""data:image/png;base64,{LogoBase64}"" width=""34"" height=""29"" alt=""WinPlus"" style=""display:block;width:34px;height:auto;"">
                  </td>
                  <td style=""vertical-align:middle;"">
                    <span style=""font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;font-size:20px;font-weight:700;color:#0F2A35;letter-spacing:-0.02em;"">Win<em style=""font-family:'Instrument Serif',Georgia,serif;font-style:italic;color:#259A8E;"">+</em></span>
                  </td>
                </tr>
              </table>
            </td>
          </tr>

          <!-- Carte principale -->
          <tr>
            <td class=""card-padding"" style=""background-color:#FFFFFF;border-radius:24px;padding:48px 44px 40px;box-shadow:0 12px 32px rgba(23,65,82,0.10);"">

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                <tr><td align=""center"" style=""padding:0 0 16px;"">
                  <span style=""font-family:Arial,sans-serif;font-size:12px;font-weight:700;color:{accentColor};letter-spacing:0.09em;text-transform:uppercase;"">{EscapeHtml(eyebrow)}</span>
                </td></tr>
              </table>

              <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
                <tr><td align=""center"" style=""padding:0 0 28px;"">
                  <h1 class=""headline"" style=""margin:0;font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;font-size:26px;line-height:1.25;font-weight:700;color:#0F2A35;letter-spacing:-0.02em;"">{EscapeHtml(headline)}</h1>
                </td></tr>
              </table>

              {bodyHtml}

            </td>
          </tr>

          <!-- Pied de page -->
          <tr>
            <td align=""center"" style=""padding:32px 24px 8px;"">
              <p style=""margin:0 0 6px;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:12px;color:#97AAB2;"">
                Besoin d'aide&nbsp;? <a href=""mailto:support@winplus.cm"" style=""color:#3471A0;font-weight:500;"">support@winplus.cm</a>
              </p>
              <p style=""margin:0 0 14px;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:12px;color:#97AAB2;"">
                <a href=""https://winplus.cm/privacy"" style=""color:#97AAB2;text-decoration:underline;"">Confidentialité</a>
                &nbsp;·&nbsp;
                <a href=""https://winplus.cm"" style=""color:#97AAB2;text-decoration:underline;"">winplus.cm</a>
              </p>
              <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:11px;color:#C8D2D6;"">
                © {DateTime.UtcNow.Year} WinPlus. Tous droits réservés.
              </p>
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