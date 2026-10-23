using System.Linq.Expressions;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Application.Loyalty.Ports;
using DeBliss.Domain.Loyalty;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class LoyaltyRepository(DeBlissDbContext context) : ILoyaltyRepository
{
    public Task<LoyaltySettings?> GetSettingsAsync(bool tracked = false, CancellationToken ct = default)
    {
        var query = context.Set<LoyaltySettings>().AsQueryable();
        if (!tracked)
            query = query.AsNoTracking();
        return query.SingleOrDefaultAsync(settings => settings.Id == LoyaltySettings.SingletonId, ct);
    }

    public void AddSettings(LoyaltySettings settings) => context.Set<LoyaltySettings>().Add(settings);

    public async Task<LoyaltyTotals> GetTotalsAsync(Guid customerId, CancellationToken ct = default) =>
        await context.Set<CustomerLoyaltyTransaction>().AsNoTracking()
            .Where(transaction => transaction.CustomerId == customerId)
            .GroupBy(transaction => transaction.CustomerId)
            .Select(group => new LoyaltyTotals(
                group.Sum(transaction => transaction.Points),
                group.Sum(transaction => transaction.Type == LoyaltyTransactionType.Earn ? transaction.Points : 0m),
                -group.Sum(transaction => transaction.Type == LoyaltyTransactionType.Redeem ? transaction.Points : 0m)))
            .SingleOrDefaultAsync(ct) ?? new LoyaltyTotals(0m, 0m, 0m);

    public async Task<IReadOnlyList<LoyaltyTransactionDto>> GetRecentTransactionsAsync(
        Guid customerId, int count, CancellationToken ct = default) =>
        await History(customerId).Take(count).Select(Projection).ToListAsync(ct);

    public async Task<(IReadOnlyList<LoyaltyTransactionDto> Items, int TotalCount)> ListTransactionsAsync(
        Guid customerId, int page, int pageSize, CancellationToken ct = default)
    {
        var query = History(customerId);
        var totalCount = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).Select(Projection).ToListAsync(ct);
        return (items, totalCount);
    }

    public void AddTransaction(CustomerLoyaltyTransaction transaction) =>
        context.Set<CustomerLoyaltyTransaction>().Add(transaction);

    private IOrderedQueryable<CustomerLoyaltyTransaction> History(Guid customerId) =>
        // Deleted invoices still identify their immutable loyalty entries.
        context.Set<CustomerLoyaltyTransaction>().AsNoTracking().IgnoreQueryFilters()
            .Where(transaction => transaction.CustomerId == customerId)
            .OrderByDescending(transaction => transaction.OccurredAt)
            .ThenByDescending(transaction => transaction.Id);

    private static readonly Expression<Func<CustomerLoyaltyTransaction, LoyaltyTransactionDto>> Projection =
        transaction => new LoyaltyTransactionDto(
            transaction.Id, transaction.Type.ToString(), transaction.CustomerId, transaction.InvoiceId,
            transaction.Invoice.InvoiceNumber, transaction.Points, transaction.OccurredAt, transaction.Description,
            transaction.EligibleSpend, transaction.EarningPercentage, transaction.PointValueAed,
            transaction.CreatedByUserId, transaction.IdempotencyKey, transaction.ReversesTransactionId);
}
