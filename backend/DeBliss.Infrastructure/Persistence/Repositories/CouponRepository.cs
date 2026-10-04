using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Billing.Ports;
using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Coupon persistence for the billing module. GetByIdAsync returns a tracked
/// coupon because the application service mutates what it loads; the code
/// lookup and the list query do not track. Codes are compared on the stored
/// form — trimmed and upper-cased by the application — so equality is exact
/// and the unique index does the real duplicate prevention.
/// </summary>
public sealed class CouponRepository(DeBlissDbContext context) : ICouponRepository
{
    public Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return context.Coupons.FirstOrDefaultAsync(coupon => coupon.Id == id, ct);
    }

    public Task<Coupon?> GetByCodeAsync(string normalizedCode, CancellationToken ct = default)
    {
        return context.Coupons
            .AsNoTracking()
            .FirstOrDefaultAsync(coupon => coupon.Code == normalizedCode, ct);
    }

    public async Task<bool> CodeExistsAsync(
        string normalizedCode,
        Guid? excludeId,
        CancellationToken ct = default)
    {
        var matches = context.Coupons
            .AsNoTracking()
            .Where(coupon => coupon.Code == normalizedCode);

        if (excludeId is { } id)
        {
            matches = matches.Where(coupon => coupon.Id != id);
        }

        return await matches.AnyAsync(ct);
    }

    public async Task<(IReadOnlyList<Coupon> Items, int TotalCount)> SearchAsync(
        CouponQuery query,
        CancellationToken ct = default)
    {
        var filtered = ApplyFilters(context.Coupons.AsNoTracking(), query);

        var totalCount = await filtered.CountAsync(ct);

        var items = await filtered
            .OrderBy(coupon => coupon.Code)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .ToListAsync(ct);

        return (items, totalCount);
    }

    public void Add(Coupon coupon) => context.Coupons.Add(coupon);

    private static IQueryable<Coupon> ApplyFilters(IQueryable<Coupon> query, CouponQuery normalized)
    {
        if (normalized.Search is { Length: > 0 } term)
        {
            var lowered = term.ToLowerInvariant();

            query = query.Where(coupon => coupon.Code.ToLower().Contains(lowered));
        }

        return normalized.Status switch
        {
            CouponStatusFilter.Active => query.Where(coupon => coupon.IsActive),
            CouponStatusFilter.Inactive => query.Where(coupon => !coupon.IsActive),
            _ => query
        };
    }
}
