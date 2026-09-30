namespace Backend.Models.DTOs;

public record AffiliateAccountDto(
    int Id,
    string Code,
    string ReferralUrl,
    decimal CommissionRate,
    string Status,
    DateTime CreatedAt);

public record AffiliateStatsDto(
    AffiliateAccountDto Account,
    int TotalClicks,
    int TotalConversions,
    decimal PendingEarnings,
    decimal ConfirmedEarnings,
    decimal PaidEarnings);

public record AffiliateCommissionDto(
    int Id,
    int OrderId,
    string OrderNumber,
    decimal OrderAmount,
    decimal CommissionRateApplied,
    decimal CommissionAmount,
    string Status,
    DateTime CreatedAt,
    DateTime? ConfirmedAt);

public record TrackAffiliateClickRequest(string Code, string VisitorToken, string? LandingPath);

public record AffiliateSettingsDto(
    decimal CommissionRateCapPercent,
    int AttributionWindowDays,
    int HoldPeriodDays);

public record UpdateAffiliateSettingsRequest(
    decimal CommissionRateCapPercent,
    int AttributionWindowDays,
    int HoldPeriodDays);

public record AdminAffiliateAccountDto(
    int Id,
    int UserId,
    string TeacherName,
    string Code,
    decimal CommissionRate,
    string Status,
    int TotalClicks,
    int TotalConversions,
    decimal TotalCommissionsPaidOut,
    DateTime CreatedAt,
    DateTime? LastRateUpdateAt);

/// <summary>
/// Signaux d'un affilié utilisés par AffiliateService.ComputeHeuristicRate
/// pour recalculer son taux de commission. Module 23 : ils étaient aussi
/// envoyés à une route WinAI (/api/affiliate/commission-rate) qui n'a jamais
/// existé côté Python ; cet appel mort et sa réponse ont été retirés.
/// </summary>
public record AffiliateRateSignals(
    int AffiliateUserId,
    int TenureDays,
    int TotalClicks,
    int TotalConversions,
    decimal ConversionRate,
    decimal TotalRevenueGenerated,
    decimal RefundRatePercent,
    decimal CurrentRate,
    decimal CapPercent);

