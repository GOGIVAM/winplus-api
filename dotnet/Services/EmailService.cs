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
    Task<bool> SendPaymentConfirmationAsync(string email, string firstName, decimal amount, string reference, DateTime completedAt, IEnumerable<(string Title, int SubjectId)>? items = null);
    Task<bool> SendSubscriptionExpiryReminderAsync(string email, string firstName, DateTime expiryDate);
    Task<bool> SendPeriodicConfirmationAsync(string email, string firstName, string code);
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
    public async Task<bool> SendPaymentConfirmationAsync(string email, string firstName, decimal amount, string reference, DateTime completedAt, IEnumerable<(string Title, int SubjectId)>? items = null)
    {
        var formattedAmount = amount.ToString("N0").Replace(",", " ");
        var formattedDate   = completedAt.ToString("dd/MM/yyyy à HH:mm") + " UTC";

        // ── Liste des épreuves achetées ──────────────────────────────────────
        var itemsList = items?.ToList() ?? new List<(string Title, int SubjectId)>();
        var itemsHtml = new StringBuilder();
        if (itemsList.Count > 0)
        {
            itemsHtml.Append(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td style=""padding:0 0 8px;"">
          <p style=""margin:0;font-family:Arial,sans-serif;font-size:12px;font-weight:700;letter-spacing:0.09em;color:#4E7280;text-transform:uppercase;"">Épreuves achetées</p>
        </td></tr>
      </table>");
            foreach (var (title, subjectId) in itemsList)
            {
                var subjectUrl = $"https://winplus.cm/catalog/{subjectId}";
                itemsHtml.Append($@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin-bottom:8px;"">
        <tr>
          <td style=""background-color:#F9F6EE;border:1px solid #E8E0CE;border-radius:12px;padding:14px 16px;"">
            <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr>
              <td style=""font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:14px;font-weight:600;color:#0F2A35;vertical-align:middle;"">{EscapeHtml(title)}</td>
              <td align=""right"" style=""white-space:nowrap;padding-left:12px;vertical-align:middle;"">
                <a href=""{subjectUrl}"" style=""display:inline-block;padding:7px 16px;background-color:#0F2A35;color:#6BCFC6;font-family:'Bricolage Grotesque',-apple-system,Arial,sans-serif;font-size:12px;font-weight:700;text-decoration:none;border-radius:8px;"">Télécharger →</a>
              </td>
            </tr></table>
          </td>
        </tr>
      </table>");
            }
            itemsHtml.Append(@"<table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0""><tr><td style=""padding:8px 0 24px;""></td></tr></table>");
        }

        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 4px 28px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong style=""color:#1F4A5A;"">{EscapeHtml(firstName)}</strong>, votre paiement a bien été reçu et validé. {(itemsList.Count > 0 ? "Vos épreuves sont maintenant disponibles au téléchargement." : "Vos cours sont maintenant accessibles.")}
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
              <td style=""padding:16px 0 0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;"">Montant total</td>
              <td style=""padding:16px 0 0;text-align:right;font-family:'Bricolage Grotesque',-apple-system,'Helvetica Neue',Arial,sans-serif;font-size:26px;font-weight:700;color:#0F2A35;"">{formattedAmount} <span style=""font-size:14px;color:#259A8E;"">XAF</span></td>
            </tr>
          </table>
        </td></tr>
      </table>

      {itemsHtml}

      {Divider()}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 0 8px;"">
          <p style=""margin:0 0 16px;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:13px;color:#4E7280;text-align:center;"">Retrouvez toutes vos épreuves dans votre espace personnel.</p>
          {Button("Accéder à mes épreuves →", "https://winplus.cm/dashboard")}
        </td></tr>
      </table>";

        var html = Wrapper("Paiement", "Paiement confirmé ✓", body, "#1F9D6E");
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
    //  EMAIL : Reconfirmation périodique mobile (style WhatsApp — tous les 30-45 jours)
    // ─────────────────────────────────────────────────────────────────────────
    public async Task<bool> SendPeriodicConfirmationAsync(string email, string firstName, string code)
    {
        var body = $@"
      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"">
        <tr><td align=""center"" style=""padding:0 8px 24px;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,'Helvetica Neue',Arial,sans-serif;font-size:15px;line-height:1.6;color:#4E7280;text-align:center;"">
            Bonjour <strong>{EscapeHtml(firstName)}</strong>,<br>
            pour sécuriser ton accès WinPlus, saisis ce code dans l'application.<br>
            Il expire dans <strong>10 minutes</strong>.
          </p>
        </td></tr>
      </table>

      {OtpBlock(code)}

      {InfoBox("Si tu n'as pas ouvert l'application WinPlus, ignore cet email. Ton compte est en sécurité.")}

      <table role=""presentation"" width=""100%"" cellpadding=""0"" cellspacing=""0"" border=""0"" style=""margin-top:24px;"">
        <tr><td style=""padding-top:16px;border-top:1px solid #E8E0CE;"">
          <p style=""margin:0;font-family:-apple-system,'Segoe UI',Roboto,Arial,sans-serif;font-size:12px;line-height:1.6;color:#97AAB2;text-align:center;"">
            Cette vérification est requise périodiquement pour maintenir la sécurité de ton compte.
          </p>
        </td></tr>
      </table>";

        var html = Wrapper("Vérification de sécurité", "Confirme ton identité", body);
        return await SendGenericEmailAsync(email, "Ton code de confirmation WinPlus", html);
    }

    // ─────────────────────────────────────────────────────────────────────────
    //  IDENTITÉ VISUELLE — logo WinPlus encodé en base64 (autonome, sans hébergement)
    // ─────────────────────────────────────────────────────────────────────────
    private const string LogoBase64 = "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";

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