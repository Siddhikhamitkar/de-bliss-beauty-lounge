using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Domain.Loyalty;

namespace DeBliss.Application.Loyalty.Ports;

public sealed record LoyaltyTotals(decimal Balance, decimal LifetimePointsEarned, decimal PointsRedeemed);

public interface ILoyaltyRepository
{
    Task<LoyaltySettings?> GetSettingsAsync(bool tracked = false, CancellationToken ct = default);
    void AddSettings(LoyaltySettings settings);
    Task<LoyaltyTotals> GetTotalsAsync(Guid customerId, CancellationToken ct = default);
    Task<IReadOnlyList<LoyaltyTransactionDto>> GetRecentTransactionsAsync(
        Guid customerId, int count, CancellationToken ct = default);
    Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
        Guid customerId, int page, int pageSize, CancellationToken ct = default);
    void AddTransaction(CustomerLoyaltyTransaction transaction);
}
