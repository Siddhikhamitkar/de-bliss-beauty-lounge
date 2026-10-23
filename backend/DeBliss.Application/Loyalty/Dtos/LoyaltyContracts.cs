namespace DeBliss.Application.Loyalty.Dtos;

public sealed record LoyaltySettingsDto(
    bool IsEnabled,
    decimal EarningPercentage,
    decimal PointValueAed,
    decimal MinimumRedemptionPoints,
    decimal MaximumRedemptionPercentage,
    bool AllowPartialRedemption,
    decimal? MinimumInvoiceAmount);

public sealed record SaveLoyaltySettingsRequest(
    bool? IsEnabled,
    decimal? EarningPercentage,
    decimal? PointValueAed,
    decimal? MinimumRedemptionPoints,
    decimal? MaximumRedemptionPercentage,
    bool? AllowPartialRedemption,
    decimal? MinimumInvoiceAmount = null);

public sealed record LoyaltyTransactionDto(
    Guid Id,
    string Type,
    Guid CustomerId,
    Guid InvoiceId,
    string? InvoiceNumber,
    decimal Points,
    DateTimeOffset OccurredAt,
    string Description,
    decimal EligibleSpend,
    decimal EarningPercentage,
    decimal PointValueAed,
    Guid? CreatedByUserId,
    string IdempotencyKey,
    Guid? ReversesTransactionId);

public sealed record CustomerLoyaltySummaryDto(
    Guid CustomerId,
    decimal AvailablePoints,
    decimal LifetimePointsEarned,
    decimal PointsRedeemed,
    decimal PointsDebt,
    IReadOnlyList<LoyaltyTransactionDto> RecentTransactions);

public sealed record LoyaltyQuoteDto(
    bool IsEnabled,
    decimal AvailablePoints,
    decimal AvailableValue,
    decimal PointValueAed,
    decimal MaximumRedeemablePoints,
    decimal MaximumDiscount,
    decimal MinimumRedemptionPoints,
    decimal MaximumRedemptionPercentage,
    bool AllowPartialRedemption,
    decimal? MinimumInvoiceAmount,
    string? UnavailableReason,
    decimal PointsToRedeem,
    decimal Discount,
    decimal EligibleSpend,
    decimal PointsEarned);
