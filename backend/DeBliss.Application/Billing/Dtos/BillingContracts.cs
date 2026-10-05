namespace DeBliss.Application.Billing.Dtos;

/// <summary>How the invoice list is narrowed by record state. Sent by name, as the API binds it.</summary>
public enum InvoiceStatusFilter
{
    All = 0,
    Draft = 1,
    Paid = 2
}

/// <summary>
/// How the invoice list is narrowed by tender type. Phase 1 has exactly three
/// payment methods; split payments do not exist, so a single value per invoice.
/// </summary>
public enum InvoicePaymentMethodFilter
{
    All = 0,
    Cash = 1,
    Card = 2,
    BankTransfer = 3
}

/// <summary>
/// Everything the invoice list can be narrowed by. Page and page size are
/// normalised before they reach the database so a caller cannot ask for page
/// zero or an unbounded page.
/// </summary>
public sealed record InvoiceQuery(
    string? Search,
    InvoiceStatusFilter Status,
    InvoicePaymentMethodFilter PaymentMethod,
    Guid? CustomerId,
    int Page,
    int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public InvoiceQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}

/// <summary>One row in the billing list: enough to recognise an invoice and spot what it owes.</summary>
public sealed record InvoiceSummaryDto(
    Guid Id,
    string InvoiceNumber,
    Guid? CustomerId,
    string? CustomerName,
    Guid? AppointmentId,
    string Status,
    string Currency,
    decimal SubTotal,
    decimal DiscountTotal,
    decimal TotalAmount,
    string? PaymentMethod,
    DateTimeOffset? IssuedAt,
    DateTimeOffset CreatedAt);

/// <summary>
/// One charged line on an invoice. The description and unit price are the
/// checkout-time snapshot, so a reprint months later shows what was actually
/// charged even if the catalogue price has moved on since.
/// </summary>
public sealed record InvoiceItemDto(
    Guid Id,
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal,
    Guid? ServiceId,
    Guid? AppointmentServiceId,
    int SortOrder);

/// <summary>One tendered payment against an invoice.</summary>
public sealed record InvoicePaymentDto(
    Guid Id,
    decimal Amount,
    string Currency,
    string Method,
    string Status,
    string? Reference,
    DateTimeOffset PaidAt);

/// <summary>
/// The full invoice as the details drawer and the checkout screen render it.
/// A bundle booking bills its components as ordinary lines and carries the
/// bundle's own saving in <see cref="BundleDiscount"/>, so the components stay
/// visible on the bill while the amount owed is the bundle price.
/// </summary>
public sealed record InvoiceDto(
    Guid Id,
    string InvoiceNumber,
    string Status,
    string Currency,
    Guid? AppointmentId,
    Guid? CustomerId,
    string? CustomerName,
    string? WalkInCustomerName,
    Guid? CouponId,
    string? CouponCode,
    Guid? ServiceBundleId,
    string? BundleName,
    decimal SubTotal,
    decimal BundleDiscount,
    decimal CouponDiscount,
    decimal ManualDiscount,
    decimal DiscountTotal,
    decimal TotalAmount,
    DateTimeOffset? IssuedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt,
    IReadOnlyList<InvoiceItemDto> Items,
    IReadOnlyList<InvoicePaymentDto> Payments);

/// <summary>
/// Body of "start a checkout for this appointment". The checkout screen posts
/// this on load; the backend returns the existing open invoice if there is one,
/// so refreshing mid-checkout never creates a duplicate bill.
/// </summary>
public sealed record CreateInvoiceRequest(Guid AppointmentId);

/// <summary>
/// The result of starting a checkout. <see cref="WasCreated"/> tells the caller
/// whether a new draft was raised, which is only interesting to whoever cares
/// about "first visit of the day" style statistics — the screen renders the
/// same either way.
/// </summary>
public sealed record InvoiceStartResult(InvoiceDto Invoice, bool WasCreated);

/// <summary>
/// Body of "complete the payment". Every field is nullable because the
/// receptionist fills the form incrementally and the server owns the rules:
/// each missing or invalid value comes back as a field-level validation error
/// rather than a prompt the frontend had to duplicate.
/// </summary>
public sealed record CheckoutRequest(
    string? CouponCode,
    string? ManualDiscountType,
    decimal? ManualDiscountValue,
    string? PaymentMethod,
    decimal? Amount,
    string? Reference);

/// <summary>One printable line on the receipt.</summary>
public sealed record ReceiptLineDto(
    string Description,
    decimal Quantity,
    decimal UnitPrice,
    decimal LineTotal);

/// <summary>
/// The printable receipt. Assembled server-side from the stored invoice and its
/// completed payment so the numbers are exactly the ones that were charged. A
/// bundle booking prints its component lines and then the bundle saving as its
/// own reduction, so the customer sees what they received and what it cost.
/// </summary>
public sealed record ReceiptDto(
    string SalonName,
    Guid InvoiceId,
    string InvoiceNumber,
    string? CustomerName,
    IReadOnlyList<ReceiptLineDto> Items,
    decimal SubTotal,
    string? BundleName,
    decimal BundleDiscount,
    string? CouponCode,
    decimal CouponDiscount,
    decimal ManualDiscount,
    decimal TotalAmount,
    string Currency,
    string PaymentMethod,
    string? PaymentReference,
    DateTimeOffset PaidAt,
    DateTimeOffset? IssuedAt);

/// <summary>How the coupon list is narrowed by state. Sent by name, as the API binds it.</summary>
public enum CouponStatusFilter
{
    All = 0,
    Active = 1,
    Inactive = 2
}

/// <summary>
/// Everything the coupon list can be narrowed by, normalised the same way the
/// other list queries in the system are.
/// </summary>
public sealed record CouponQuery(string? Search, CouponStatusFilter Status, int Page, int PageSize)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    public CouponQuery Normalized()
    {
        var pageSize = PageSize <= 0 ? DefaultPageSize : Math.Min(PageSize, MaximumPageSize);

        return this with
        {
            Search = string.IsNullOrWhiteSpace(Search) ? null : Search.Trim(),
            Page = Math.Max(Page, 1),
            PageSize = pageSize
        };
    }
}

/// <summary>One coupon as the management screen lists it.</summary>
public sealed record CouponDto(
    Guid Id,
    string Code,
    string DiscountType,
    decimal DiscountValue,
    bool IsActive,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

/// <summary>
/// Payload for creating and editing a coupon. Codes are stored trimmed and
/// upper-cased so matching is case-insensitive and a code can exist only once.
/// <see cref="IsActive"/> is nullable so an edit can leave it untouched.
/// </summary>
public sealed record SaveCouponRequest(
    string? Code,
    string? DiscountType,
    decimal? DiscountValue,
    DateOnly? ValidFrom,
    DateOnly? ValidUntil,
    bool? IsActive);

/// <summary>Body of the coupon activate/deactivate endpoint.</summary>
public sealed record SetCouponStatusRequest(bool IsActive);

/// <summary>
/// Body of the coupon preview the checkout screen calls before applying.
/// The subtotal is supplied so a percentage coupon can be previewed; the real
/// discount is recomputed server-side from the stored invoice at payment time.
/// </summary>
public sealed record ValidateCouponRequest(string? Code, decimal Subtotal);

/// <summary>The effect of a coupon, as the checkout screen shows it while the invoice is still open.</summary>
public sealed record CouponValidationDto(
    string Code,
    string DiscountType,
    decimal DiscountValue,
    decimal DiscountAmount);
