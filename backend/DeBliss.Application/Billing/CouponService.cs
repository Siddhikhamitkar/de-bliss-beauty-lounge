using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Billing.Ports;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Domain.Billing;

namespace DeBliss.Application.Billing;

/// <summary>
/// Coupon management and validation. Codes are stored trimmed and upper-cased
/// so matching is case-insensitive and a code can exist only once — the unique
/// index is the real guarantee, the pre-check here just turns a database error
/// into a readable conflict. Coupons are retired by deactivating them and are
/// never deleted, so a paid invoice that used one can always be explained.
/// </summary>
public sealed class CouponService(
    ICouponRepository coupons,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICouponService
{
    public async Task<PagedResult<CouponDto>> SearchAsync(
        CouponQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var (items, totalCount) = await coupons.SearchAsync(normalized, cancellationToken);

        return new PagedResult<CouponDto>(
            [.. items.Select(Map)],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    public async Task<CouponDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var coupon = await LoadAsync(id, cancellationToken);

        return Map(coupon);
    }

    public async Task<CouponDto> CreateAsync(
        SaveCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateForSaveAsync(request, excludeId: null, cancellationToken);

        var coupon = new Coupon
        {
            Code = validated.Code,
            DiscountType = validated.DiscountType,
            DiscountValue = validated.DiscountValue,
            ValidFrom = request.ValidFrom,
            ValidUntil = request.ValidUntil,
            IsActive = request.IsActive ?? true
        };

        coupons.Add(coupon);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(coupon);
    }

    public async Task<CouponDto> UpdateAsync(
        Guid id,
        SaveCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateForSaveAsync(request, excludeId: id, cancellationToken);

        var coupon = await LoadAsync(id, cancellationToken);

        coupon.Code = validated.Code;
        coupon.DiscountType = validated.DiscountType;
        coupon.DiscountValue = validated.DiscountValue;
        coupon.ValidFrom = request.ValidFrom;
        coupon.ValidUntil = request.ValidUntil;

        if (request.IsActive is not null)
        {
            coupon.IsActive = request.IsActive.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(coupon);
    }

    public async Task<CouponDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var coupon = await LoadAsync(id, cancellationToken);

        coupon.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(coupon);
    }

    public async Task<CouponValidationDto> ValidateAsync(
        ValidateCouponRequest request,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        if (request.Subtotal < 0m)
        {
            errors.Add("subtotal", "Subtotal cannot be negative.");
        }

        var code = request.Code?.Trim() ?? string.Empty;

        if (code.Length == 0)
        {
            errors.Add("code", "Coupon code is invalid or expired.");
        }

        errors.ThrowIfInvalid();

        var normalized = code.ToUpperInvariant();
        var coupon = await coupons.GetByCodeAsync(normalized, cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        // One message covers unknown, deactivated and out-of-window alike: the
        // receptionist cannot act on the difference and the customer must not
        // learn which codes exist.
        if (coupon is null || !CouponRules.IsApplicable(coupon, today))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["Coupon code is invalid or expired."]
            });
        }

        var discount = DiscountMath.Compute(request.Subtotal, coupon.DiscountType, coupon.DiscountValue);

        if (discount > request.Subtotal)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["code"] = ["Discount cannot exceed the subtotal."]
            });
        }

        return new CouponValidationDto(
            coupon.Code,
            coupon.DiscountType.ToString(),
            coupon.DiscountValue,
            discount);
    }

    private async Task<Coupon> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await coupons.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Coupon '{id}' was not found.");
    }

    private async Task<ValidatedCoupon> ValidateForSaveAsync(
        SaveCouponRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        // The stored form is trimmed and upper-cased; validating the stored
        // form is what keeps the unique index and the duplicate check aligned.
        var code = request.Code?.Trim().ToUpperInvariant() ?? string.Empty;

        if (code.Length == 0)
        {
            errors.Add("code", "Coupon code is required.");
        }
        else if (code.Length > BillingLimits.CouponCodeMaxLength)
        {
            errors.Add("code", $"Coupon code must be {BillingLimits.CouponCodeMaxLength} characters or fewer.");
        }

        DiscountType? discountType = null;

        if (string.IsNullOrWhiteSpace(request.DiscountType)
            || !Enum.TryParse<DiscountType>(request.DiscountType.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            errors.Add("discountType", "Select Fixed or Percentage.");
        }
        else
        {
            discountType = parsed;
        }

        var discountValue = request.DiscountValue;

        if (discountValue is null)
        {
            errors.Add("discountValue", "Enter a discount value.");
        }
        else if (discountType == DiscountType.Percentage && (discountValue <= 0m || discountValue > 100m))
        {
            errors.Add("discountValue", "Enter a percentage between 0 and 100.");
        }
        else if (discountType == DiscountType.Fixed && discountValue <= 0m)
        {
            errors.Add("discountValue", "Enter a discount amount greater than zero.");
        }
        else if (discountValue < 0m)
        {
            errors.Add("discountValue", "Enter a discount amount greater than zero.");
        }

        if (request.ValidFrom is { } validFrom
            && request.ValidUntil is { } validUntil
            && validUntil < validFrom)
        {
            errors.Add("validUntil", "Valid until must be on or after valid from.");
        }

        errors.ThrowIfInvalid();

        if (await coupons.CodeExistsAsync(code, excludeId, cancellationToken))
        {
            throw new ConflictException($"A coupon with the code '{code}' already exists.");
        }

        return new ValidatedCoupon(code, discountType!.Value, discountValue!.Value);
    }

    private static CouponDto Map(Coupon coupon) => new(
        coupon.Id,
        coupon.Code,
        coupon.DiscountType.ToString(),
        coupon.DiscountValue,
        coupon.IsActive,
        coupon.ValidFrom,
        coupon.ValidUntil,
        coupon.CreatedAt,
        coupon.UpdatedAt);

    private sealed record ValidatedCoupon(string Code, DiscountType DiscountType, decimal DiscountValue);
}
