using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Paging;

namespace DeBliss.Application.Billing;

public interface ICouponService
{
    Task<PagedResult<CouponDto>> SearchAsync(CouponQuery query, CancellationToken cancellationToken = default);

    Task<CouponDto> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task<CouponDto> CreateAsync(SaveCouponRequest request, CancellationToken cancellationToken = default);

    Task<CouponDto> UpdateAsync(Guid id, SaveCouponRequest request, CancellationToken cancellationToken = default);

    Task<CouponDto> SetStatusAsync(Guid id, bool isActive, CancellationToken cancellationToken = default);

    /// <summary>
    /// Preview of what a coupon would take off a given subtotal. The checkout
    /// screen calls this when the receptionist presses Apply; the validity
    /// rules live here, never in the browser.
    /// </summary>
    Task<CouponValidationDto> ValidateAsync(ValidateCouponRequest request, CancellationToken cancellationToken = default);
}
