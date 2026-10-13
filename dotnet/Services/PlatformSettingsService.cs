using System.Globalization;
using Backend.Data;
using Backend.Models.Entities;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

/// <summary>
/// Définition d'un réglage de plateforme (lot 9.7a, suivi 18.2). Seuls des
/// réglages RÉELLEMENT appliqués par le code figurent ici : chaque entrée
/// nomme l'endroit qui la lit (<see cref="AppliedBy"/>). Une valeur absente
/// de la table PlatformSettings retombe sur la clé de configuration
/// historique (<see cref="ConfigKey"/>, appsettings ou variable
/// d'environnement), puis sur <see cref="DefaultValue"/>.
/// </summary>
public sealed record PlatformSettingDefinition(
    string Key,
    string Type,
    string Category,
    string Label,
    string Description,
    string DefaultValue,
    string AppliedBy,
    decimal? Min = null,
    decimal? Max = null,
    string? ConfigKey = null,
    int MaxLength = 500);

public static class PlatformSettingTypes
{
    public const string Bool = "bool";
    public const string Int = "int";
    public const string Decimal = "decimal";
    public const string String = "string";
}

/// <summary>Catalogue des réglages et de leurs bornes (seule source).</summary>
public static class PlatformSettingDefinitions
{
    public const string MaintenanceMode = "platform.maintenance_mode";
    public const string MaintenanceMessage = "platform.maintenance_message";
    public const string SignupEnabled = "platform.signup_enabled";
    public const string ForumPostingEnabled = "features.forum_posting_enabled";
    public const string PromoCodesEnabled = "features.promo_codes_enabled";
    public const string WinAiChatTemperature = "winai.chat.temperature";
    public const string WinAiChatMaxTokens = "winai.chat.max_tokens";
    public const string TutorDisputeWindowHours = "tutor.dispute_window_hours";
    public const string NotchPayFeePercent = "payments.notchpay_fee_percent";
    public const string TutorReportThreshold = "tutor.report_threshold";
    public const string TutorReportWindowDays = "tutor.report_window_days";
    public const string IdentityRetentionApproved = "tutor.identity_retention_days_approved";
    public const string IdentityRetentionRejected = "tutor.identity_retention_days_rejected";

    public static readonly IReadOnlyList<PlatformSettingDefinition> All = new[]
    {
        new PlatformSettingDefinition(MaintenanceMode, PlatformSettingTypes.Bool, "platform", "Mode maintenance",
            "Refuse les requêtes de l'API (réponse 503) à tous les comptes sauf aux administrateurs, et affiche une page de maintenance. La connexion reste ouverte pour que l'administration puisse le désactiver.",
            "false", "MaintenanceMiddleware (toute requête /api)"),
        new PlatformSettingDefinition(MaintenanceMessage, PlatformSettingTypes.String, "platform", "Message de maintenance",
            "Texte affiché aux utilisateurs pendant la maintenance (vide : message par défaut).",
            "", "MaintenanceMiddleware et GET /api/platform/status", MaxLength: 300),
        new PlatformSettingDefinition(SignupEnabled, PlatformSettingTypes.Bool, "platform", "Nouvelles inscriptions",
            "Autorise la création de nouveaux comptes (inscription par e-mail et première connexion Google). Désactivé : les comptes existants se connectent normalement, les créations sont refusées.",
            "true", "CustomAuthService.RegisterAsync et OAuth (création de compte)"),
        new PlatformSettingDefinition(ForumPostingEnabled, PlatformSettingTypes.Bool, "features", "Publication sur le forum",
            "Autorise la création de sujets et de réponses sur le forum. Désactivé : le forum reste lisible.",
            "true", "ForumController (création de sujet et de réponse)"),
        new PlatformSettingDefinition(PromoCodesEnabled, PlatformSettingTypes.Bool, "features", "Codes promo",
            "Autorise l'utilisation des codes promo au paiement. Désactivé : tout code est refusé.",
            "true", "PromoCodeService (validation et application)"),
        new PlatformSettingDefinition(WinAiChatTemperature, PlatformSettingTypes.Decimal, "winai", "Température du chat WinAI",
            "Créativité des réponses du chat (0 = réponses les plus constantes, 1,5 = les plus variées).",
            "0.7", "ChatbotService (requête envoyée au moteur WinAI)", 0m, 1.5m, "Chatbot:Temperature"),
        new PlatformSettingDefinition(WinAiChatMaxTokens, PlatformSettingTypes.Int, "winai", "Longueur maximale d'une réponse du chat",
            "Nombre maximal de tokens générés par réponse du chat WinAI (le quota mensuel de chaque plan reste appliqué en plus).",
            "2000", "ChatbotService (requête envoyée au moteur WinAI)", 256m, 8000m, "Chatbot:MaxTokens"),
        new PlatformSettingDefinition(TutorDisputeWindowHours, PlatformSettingTypes.Int, "tutoring", "Délai de contestation d'une séance (heures)",
            "Durée pendant laquelle le payeur peut contester une séance après sa fin ; l'argent du tuteur est libéré ensuite.",
            "24", "TutorPolicySettings (réservations, engagements, libération)", 1m, 168m, "TutorBooking:DisputeWindowHours"),
        new PlatformSettingDefinition(NotchPayFeePercent, PlatformSettingTypes.Decimal, "payments", "Frais Mobile Money (%)",
            "Frais NotchPay d'un encaissement, en pourcentage, retenus selon la règle du 9.2 lors d'un remboursement au portefeuille (tutorat et commande catalogue). À aligner sur le contrat NotchPay.",
            "3", "TutorPolicySettings et CatalogRefundService", 0m, 10m, "TutorBooking:NotchPayFeePercent"),
        new PlatformSettingDefinition(TutorReportThreshold, PlatformSettingTypes.Int, "tutoring", "Seuil de signalements avant suspension du présentiel",
            "Le présentiel d'un tuteur est suspendu au-delà de ce nombre de signalements sur la fenêtre ci-dessous.",
            "3", "TutorVerificationSettings (TutorReportService)", 1m, 50m, "TutorSafety:ReportThreshold"),
        new PlatformSettingDefinition(TutorReportWindowDays, PlatformSettingTypes.Int, "tutoring", "Fenêtre des signalements (jours)",
            "Période glissante sur laquelle les signalements d'un tuteur sont comptés.",
            "30", "TutorVerificationSettings (TutorReportService)", 1m, 365m, "TutorSafety:ReportWindowDays"),
        new PlatformSettingDefinition(IdentityRetentionApproved, PlatformSettingTypes.Int, "retention", "Conservation des pièces d'identité validées (jours)",
            "Délai avant suppression des fichiers d'identité après validation (la décision reste).",
            "30", "TutorVerificationSettings (purge des pièces)", 0m, 365m, "TutorVerification:IdentityRetentionDaysAfterApproval"),
        new PlatformSettingDefinition(IdentityRetentionRejected, PlatformSettingTypes.Int, "retention", "Conservation des pièces d'identité refusées (jours)",
            "Délai avant suppression des fichiers d'identité après un refus.",
            "7", "TutorVerificationSettings (purge des pièces)", 0m, 365m, "TutorVerification:IdentityRetentionDaysAfterRejection"),
    };

    private static readonly Dictionary<string, PlatformSettingDefinition> ByKey = All.ToDictionary(d => d.Key);

    public static PlatformSettingDefinition? Find(string key) => ByKey.TryGetValue(key, out var d) ? d : null;

    /// <summary>
    /// Valide et normalise une valeur saisie. Renvoie la valeur sérialisée
    /// (texte invariant), ou une erreur en français.
    /// </summary>
    public static (string? Value, string? Error) Normalize(PlatformSettingDefinition def, string? raw)
    {
        raw = (raw ?? string.Empty).Trim();
        switch (def.Type)
        {
            case PlatformSettingTypes.Bool:
                if (raw.Equals("true", StringComparison.OrdinalIgnoreCase) || raw == "1") return ("true", null);
                if (raw.Equals("false", StringComparison.OrdinalIgnoreCase) || raw == "0") return ("false", null);
                return (null, "Valeur attendue : activé ou désactivé.");
            case PlatformSettingTypes.Int:
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return (null, "Nombre entier attendu.");
                if (def.Min.HasValue && i < def.Min.Value || def.Max.HasValue && i > def.Max.Value)
                    return (null, $"Valeur attendue entre {def.Min} et {def.Max}.");
                return (i.ToString(CultureInfo.InvariantCulture), null);
            case PlatformSettingTypes.Decimal:
                if (!decimal.TryParse(raw.Replace(',', '.'), NumberStyles.Number, CultureInfo.InvariantCulture, out var d))
                    return (null, "Nombre attendu.");
                if (def.Min.HasValue && d < def.Min.Value || def.Max.HasValue && d > def.Max.Value)
                    return (null, $"Valeur attendue entre {def.Min?.ToString(CultureInfo.InvariantCulture)} et {def.Max?.ToString(CultureInfo.InvariantCulture)}.");
                return (d.ToString(CultureInfo.InvariantCulture), null);
            default:
                if (raw.Length > def.MaxLength) return (null, $"{def.MaxLength} caractères au maximum.");
                return (raw, null);
        }
    }
}

/// <summary>
/// Lecture des réglages de plateforme (lot 9.7a, suivi 18.2) : singleton à
/// cache court (30 s), invalidé immédiatement à chaque modification par
/// l'administration. Lecture synchrone volontaire : les consommateurs (ex.
/// <see cref="TutorPolicySettings.From(IConfiguration?, IPlatformSettings?)"/>)
/// sont des fabriques synchrones appelées à la construction des services.
/// Si la table n'existe pas encore (script SQL non passé), les valeurs par
/// défaut s'appliquent et l'incident est journalisé une fois par fenêtre.
/// </summary>
public interface IPlatformSettings
{
    bool GetBool(string key);
    int GetInt(string key);
    decimal GetDecimal(string key);
    string GetString(string key);

    /// <summary>Valeur effective sérialisée (base, sinon configuration, sinon défaut).</summary>
    string GetRaw(string key);

    /// <summary>La valeur vient-elle de la table (choix d'un administrateur) ?</summary>
    bool IsOverridden(string key);

    void Invalidate();
}

public class PlatformSettings : IPlatformSettings
{
    private static readonly TimeSpan CacheWindow = TimeSpan.FromSeconds(30);

    private readonly IServiceScopeFactory? _scopeFactory;
    private readonly IConfiguration? _configuration;
    private readonly ILogger<PlatformSettings>? _logger;
    private readonly Func<IReadOnlyDictionary<string, string>>? _loader;
    private readonly object _gate = new();
    private IReadOnlyDictionary<string, string> _values = new Dictionary<string, string>();
    private DateTime _expiresAt = DateTime.MinValue;

    public PlatformSettings(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<PlatformSettings> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;
    }

    private PlatformSettings(Func<IReadOnlyDictionary<string, string>> loader, IConfiguration? configuration)
    {
        _loader = loader;
        _configuration = configuration;
    }

    /// <summary>Instance sans base de données (tests) : valeurs fournies telles quelles.</summary>
    public static PlatformSettings FromValues(IReadOnlyDictionary<string, string> values, IConfiguration? configuration = null) =>
        new(() => values, configuration);

    /// <summary>Instance lisant un contexte donné à chaque expiration du cache (tests d'intégration).</summary>
    public static PlatformSettings FromContext(Func<ApplicationDbContext> contextFactory, IConfiguration? configuration = null) =>
        new(() =>
        {
            using var db = contextFactory();
            return db.PlatformSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);
        }, configuration);

    public void Invalidate()
    {
        lock (_gate) _expiresAt = DateTime.MinValue;
    }

    private IReadOnlyDictionary<string, string> Snapshot()
    {
        lock (_gate)
        {
            if (DateTime.UtcNow < _expiresAt) return _values;
            try
            {
                _values = _loader != null ? _loader() : LoadFromDatabase();
            }
            catch (Exception ex)
            {
                _logger?.LogWarning("Réglages de plateforme illisibles, valeurs par défaut appliquées ({Message}). Passer Migrations/SQL_Admin97a.sql.", ex.Message);
                _values = new Dictionary<string, string>();
            }
            _expiresAt = DateTime.UtcNow + CacheWindow;
            return _values;
        }
    }

    private IReadOnlyDictionary<string, string> LoadFromDatabase()
    {
        using var scope = _scopeFactory!.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        return db.PlatformSettings.AsNoTracking().ToDictionary(s => s.Key, s => s.Value);
    }

    public bool IsOverridden(string key) => Snapshot().ContainsKey(key);

    public string GetRaw(string key)
    {
        var def = PlatformSettingDefinitions.Find(key)
            ?? throw new ArgumentException($"Réglage inconnu : {key}", nameof(key));

        if (Snapshot().TryGetValue(key, out var stored))
        {
            var (normalized, error) = PlatformSettingDefinitions.Normalize(def, stored);
            if (error == null) return normalized!;
        }

        if (def.ConfigKey != null)
        {
            var fromConfig = _configuration?[def.ConfigKey];
            if (!string.IsNullOrWhiteSpace(fromConfig))
            {
                var (normalized, error) = PlatformSettingDefinitions.Normalize(def, fromConfig);
                if (error == null) return normalized!;
                // Hors bornes dans la configuration : ramené dans les bornes plutôt qu'ignoré.
                if (def.Type is PlatformSettingTypes.Int or PlatformSettingTypes.Decimal
                    && decimal.TryParse(fromConfig, NumberStyles.Number, CultureInfo.InvariantCulture, out var n))
                {
                    var clamped = Math.Clamp(n, def.Min ?? n, def.Max ?? n);
                    return def.Type == PlatformSettingTypes.Int
                        ? ((int)clamped).ToString(CultureInfo.InvariantCulture)
                        : clamped.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        return def.DefaultValue;
    }

    public bool GetBool(string key) => GetRaw(key) == "true";

    public int GetInt(string key) => int.Parse(GetRaw(key), CultureInfo.InvariantCulture);

    public decimal GetDecimal(string key) => decimal.Parse(GetRaw(key), CultureInfo.InvariantCulture);

    public string GetString(string key) => GetRaw(key);
}

/// <summary>
/// Modification des réglages par l'administration : validation contre la
/// définition, écriture en base, invalidation du cache et journal d'audit
/// (avant / après) dans le même enregistrement.
/// </summary>
public interface IPlatformSettingsAdminService
{
    Task<IReadOnlyList<object>> ListAsync();
    Task<(bool Ok, string? Error)> UpdateAsync(string key, string? value, System.Security.Claims.ClaimsPrincipal actor, string? ip);
}

public class PlatformSettingsAdminService : IPlatformSettingsAdminService
{
    private readonly ApplicationDbContext _db;
    private readonly IPlatformSettings _settings;
    private readonly IAdminAuditService _audit;

    public PlatformSettingsAdminService(ApplicationDbContext db, IPlatformSettings settings, IAdminAuditService audit)
    {
        _db = db;
        _settings = settings;
        _audit = audit;
    }

    public async Task<IReadOnlyList<object>> ListAsync()
    {
        var rows = await _db.PlatformSettings.AsNoTracking().ToDictionaryAsync(s => s.Key);
        var editorIds = rows.Values.Where(r => r.UpdatedByUserId.HasValue).Select(r => r.UpdatedByUserId!.Value).Distinct().ToList();
        var editors = editorIds.Count == 0
            ? new Dictionary<int, string>()
            : await _db.Users.AsNoTracking().IgnoreQueryFilters().Where(u => editorIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => u.Email);

        return PlatformSettingDefinitions.All.Select(def =>
        {
            rows.TryGetValue(def.Key, out var row);
            return (object)new
            {
                key = def.Key,
                type = def.Type,
                category = def.Category,
                label = def.Label,
                description = def.Description,
                appliedBy = def.AppliedBy,
                min = def.Min,
                max = def.Max,
                maxLength = def.Type == PlatformSettingTypes.String ? def.MaxLength : (int?)null,
                defaultValue = def.DefaultValue,
                value = _settings.GetRaw(def.Key),
                isOverridden = row != null,
                updatedAt = row?.UpdatedAt,
                updatedBy = row?.UpdatedByUserId is int id && editors.TryGetValue(id, out var email) ? email : null,
            };
        }).ToList();
    }

    public async Task<(bool Ok, string? Error)> UpdateAsync(string key, string? value, System.Security.Claims.ClaimsPrincipal actor, string? ip)
    {
        var def = PlatformSettingDefinitions.Find(key);
        if (def == null) return (false, "Réglage inconnu.");

        var (normalized, error) = PlatformSettingDefinitions.Normalize(def, value);
        if (error != null) return (false, error);

        var before = _settings.GetRaw(key);
        var row = await _db.PlatformSettings.FirstOrDefaultAsync(s => s.Key == key);
        if (row == null)
        {
            row = new PlatformSetting { Key = key };
            _db.PlatformSettings.Add(row);
        }

        int? actorId = null;
        try { actorId = Backend.Extensions.ClaimsPrincipalExtensions.GetUserId(actor); } catch (UnauthorizedAccessException) { }

        row.Value = normalized!;
        row.ValueType = def.Type;
        row.Description = def.Label;
        row.UpdatedAt = DateTime.UtcNow;
        row.UpdatedByUserId = actorId;

        _audit.Add(actor, ip, AdminAuditActions.SettingChanged, "Setting", key, def.Label,
            before: new { value = before }, after: new { value = normalized });
        await _db.SaveChangesAsync();
        _settings.Invalidate();
        return (true, null);
    }
}
