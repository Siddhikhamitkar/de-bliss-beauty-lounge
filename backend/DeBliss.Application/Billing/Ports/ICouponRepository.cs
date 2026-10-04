using DeBliss.Application.Billing.Dtos;
using DeBliss.Domain.Billing;

namespace DeBliss.Application.Billing.Ports;

/// <summary>
/// Coupon persistence for the billing module. GetByIdAsync returns a tracked
/// coupon because the application service mutates what it loads; the code
/// lookup and list queries do not track. Coupons are retired by deactivating
/// them, never deleted, so there is no delete operation here.
/// </summary>
public interface ICouponRepository
{
    /// <summary>Tracked coupon or null.</summary>
    Task<Coupon?> GetByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>
    /// The coupon whose code matches, read-only. Matching is on the stored
    /// form: the application trims and upper-cases both sides, so " summer10 "
    /// and "SUMMER10" find the same row.
    /// </summary>
    Task<Coupon?> GetByCodeAsync(string normalizedCode, CancellationToken ct = default);

    /// <summary>True when another coupon already uses the code; excludeId skips the coupon being edited.</summary>
    Task<bool> CodeExistsAsync(string normalizedCode, Guid? excludeId, CancellationToken ct = default);

    Task<(IReadOnlyList<Coupon> Items, int TotalCount)> SearchAsync(CouponQuery query, CancellationToken ct = default);

    void Add(Coupon coupon);
}
