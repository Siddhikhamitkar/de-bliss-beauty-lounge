using DeBliss.Application.Common.Paging;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Billing;

namespace DeBliss.Application.Loyalty;

public interface ILoyaltyService
{
    Task<LoyaltySettingsDto> GetSettingsAsync(CancellationToken ct = default);
    Task<LoyaltySettingsDto> UpdateSettingsAsync(
        SaveLoyaltySettingsRequest request, Guid? actorUserId = null, CancellationToken ct = default);
    Task<CustomerLoyaltySummaryDto> GetSummaryAsync(Guid customerId, CancellationToken ct = default);
    Task<PagedResult<LoyaltyTransactionDto>> ListTransactionsAsync(
        Guid customerId, int page = 1, int pageSize = 50, CancellationToken ct = default);
    Task<LoyaltyCalculation> CalculateAsync(
        Guid? customerId, decimal merchandiseAmount, decimal taxRate, bool pricesIncludeTax,
        decimal pointsToRedeem, CancellationToken ct = default);

    /// <summary>Stages entries only; the caller saves them with payment in its serializable transaction.</summary>
    void RecordCheckout(Invoice invoice, LoyaltyCalculation calculation, Guid? actorUserId);
}

/// <summary>Application-only checkout state. API responses expose Quote, not Settings.</summary>
public sealed record LoyaltyCalculation(LoyaltyQuoteDto Quote, LoyaltySettingsDto Settings)
{
    internal Guid? CustomerId { get; init; }
}
