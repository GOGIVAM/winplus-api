namespace Backend.Services;

/// <summary>
/// Réglages serveur du lot 9.5 (suivi 17.11, 17.12, 17.16), lus dans la
/// configuration avec des valeurs par défaut sûres et bornées.
/// <list type="bullet">
///   <item><c>TutorSafety:ReportThreshold</c> (3) et <c>TutorSafety:ReportWindowDays</c> (30) :
///   le présentiel est suspendu AU-DELÀ du seuil, donc au 4e signalement en 30 jours par défaut ;</item>
///   <item><c>TutorVerification:IdentityRetentionDaysAfterApproval</c> (30) : fichiers d'identité
///   supprimés 30 jours après la validation (la décision, son auteur et sa date restent) ;</item>
///   <item><c>TutorVerification:IdentityRetentionDaysAfterRejection</c> (7) : 7 jours après un refus ;
///   un dépôt remplacé avant décision est supprimé tout de suite ;</item>
///   <item><c>TutorVerification:IdentityUrlTtlMinutes</c> (5) : durée des URL présignées admin.</item>
/// </list>
/// </summary>
public sealed record TutorVerificationSettings(
    int ReportThreshold,
    int ReportWindowDays,
    int IdentityRetentionDaysAfterApproval,
    int IdentityRetentionDaysAfterRejection,
    int IdentityUrlTtlMinutes)
{
    public TimeSpan ReportWindow => TimeSpan.FromDays(ReportWindowDays);
    public TimeSpan IdentityUrlTtl => TimeSpan.FromMinutes(IdentityUrlTtlMinutes);

    /// <summary>
    /// Lot 9.7a (suivi 18.2) : seuil, fenêtre et durées de conservation sont
    /// réglables dans l'admin ; la valeur choisie l'emporte sur la configuration.
    /// </summary>
    public static TutorVerificationSettings From(IConfiguration configuration, IPlatformSettings? settings) =>
        settings == null ? From(configuration) : new(
            Math.Clamp(settings.GetInt(PlatformSettingDefinitions.TutorReportThreshold), 1, 50),
            Math.Clamp(settings.GetInt(PlatformSettingDefinitions.TutorReportWindowDays), 1, 365),
            Math.Clamp(settings.GetInt(PlatformSettingDefinitions.IdentityRetentionApproved), 0, 365),
            Math.Clamp(settings.GetInt(PlatformSettingDefinitions.IdentityRetentionRejected), 0, 365),
            Math.Clamp(configuration.GetValue<int?>("TutorVerification:IdentityUrlTtlMinutes") ?? 5, 1, 15));

    public static TutorVerificationSettings From(IConfiguration configuration) => new(
        Math.Clamp(configuration.GetValue<int?>("TutorSafety:ReportThreshold") ?? 3, 1, 50),
        Math.Clamp(configuration.GetValue<int?>("TutorSafety:ReportWindowDays") ?? 30, 1, 365),
        Math.Clamp(configuration.GetValue<int?>("TutorVerification:IdentityRetentionDaysAfterApproval") ?? 30, 0, 365),
        Math.Clamp(configuration.GetValue<int?>("TutorVerification:IdentityRetentionDaysAfterRejection") ?? 7, 0, 365),
        Math.Clamp(configuration.GetValue<int?>("TutorVerification:IdentityUrlTtlMinutes") ?? 5, 1, 15));
}
