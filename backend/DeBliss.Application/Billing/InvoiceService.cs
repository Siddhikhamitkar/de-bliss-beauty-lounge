using System.Globalization;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Billing.Ports;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Billing;

/// <summary>
/// The checkout use cases behind the Billing module. A checkout starts from a
/// completed appointment — its service lines become invoice lines at the
/// booking-time price snapshots, so nothing is retyped and history is never
/// rewritten. An appointment booked as a bundle still bills every component
/// line: the bundle's saving is carried as its own discount, so the components
/// stay visible on the bill while the amount owed is the bundle price.
/// Discounts are validated here (the browser is never trusted):
/// coupon and manual discount are computed separately, the combined reduction
/// can never exceed the subtotal, and the final total can never go below zero.
/// Recording the payment and marking the invoice Paid happen in one save, so an
/// invoice is never left half checked out.
/// </summary>
public sealed class InvoiceService(
    IInvoiceRepository invoices,
    ICouponRepository coupons,
    IAppointmentRepository appointments,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IInvoiceService
{
    private static readonly PaymentMethod[] PhaseOnePaymentMethods =
    [
        PaymentMethod.Cash,
        PaymentMethod.Card,
        PaymentMethod.BankTransfer
    ];

    public async Task<PagedResult<InvoiceSummaryDto>> SearchAsync(
        InvoiceQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var (items, totalCount) = await invoices.SearchAsync(normalized, cancellationToken);

        return new PagedResult<InvoiceSummaryDto>(
            [.. items.Select(MapSummary)],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        return Map(invoice);
    }

    public async Task<InvoiceStartResult> StartFromAppointmentAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken = default)
    {
        var appointment = await appointments.GetByIdAsync(request.AppointmentId, cancellationToken);

        if (appointment is null || appointment.DeletedAt is not null)
        {
            throw new NotFoundException($"Appointment '{request.AppointmentId}' was not found.");
        }

        if (appointment.Status != AppointmentStatus.Completed)
        {
            throw new ConflictException("Only a completed appointment can be checked out.");
        }

        // Resuming an interrupted checkout must return the bill that already
        // exists, not raise a second one for the same visit.
        var existing = await invoices.GetByAppointmentIdAsync(appointment.Id, cancellationToken);

        if (existing is not null)
        {
            return new InvoiceStartResult(Map(existing), WasCreated: false);
        }

        var now = timeProvider.GetUtcNow();

        var invoice = new Invoice
        {
            InvoiceNumber = await NextInvoiceNumberAsync(now.Year, cancellationToken),
            AppointmentId = appointment.Id,
            CustomerId = appointment.CustomerId,
            Customer = appointment.Customer,
            Status = InvoiceStatus.Draft,
            Currency = BillingLimits.CurrencyCode,
            Notes = appointment.WalkInCustomerName
        };

        // The lines are snapshots of the appointment's booking-time prices: the
        // invoice keeps what was agreed even if the catalogue changes later.
        foreach (var line in appointment.Services.OrderBy(line => line.SortOrder))
        {
            invoice.Items.Add(new InvoiceItem
            {
                InvoiceId = invoice.Id,
                AppointmentServiceId = line.Id,
                ServiceId = line.ServiceId,
                ItemType = InvoiceItemType.Service,
                Description = line.ServiceNameSnapshot,
                Quantity = 1m,
                UnitPrice = line.UnitPrice,
                LineTotal = line.UnitPrice,
                SortOrder = line.SortOrder
            });
        }

        invoice.SubTotal = invoice.Items.Sum(item => item.LineTotal);

        // A bundle booking keeps billing its components as their own lines, but
        // the customer pays the bundle price: the saving against the component
        // subtotal is recorded as the bundle's own discount, never as a keyed
        // one, so the offering and any coupon stay separately visible.
        if (appointment.ServiceBundleId is not null && appointment.BundlePriceSnapshot is { } bundlePrice)
        {
            invoice.ServiceBundleId = appointment.ServiceBundleId;
            invoice.BundleNameSnapshot = appointment.ServiceBundle?.Name;
            invoice.BundleDiscount = Math.Max(invoice.SubTotal - bundlePrice, 0m);
        }

        invoice.TotalAmount = invoice.SubTotal - invoice.BundleDiscount;

        // Keeps the draft's own arithmetic honest: total = subtotal − discount
        // total must hold before checkout too, or the invoice list reports a
        // bundle visit as undiscounted while charging the bundle price.
        invoice.DiscountTotal = invoice.BundleDiscount;

        // Added through the navigation so EF fixes up the line foreign keys
        // when it assigns the invoice's generated key.
        invoices.Add(invoice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new InvoiceStartResult(Map(invoice), WasCreated: true);
    }

    public async Task<ReceiptDto> CheckoutAsync(
        Guid id,
        CheckoutRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new ConflictException($"Invoice '{invoice.InvoiceNumber}' has already been paid.");
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new ConflictException($"Invoice '{invoice.InvoiceNumber}' is not awaiting checkout.");
        }

        var errors = new ValidationErrorBuilder();

        // A keyed discount reduces what is still owed after the bundle's own
        // saving, so a percentage coupon is computed against the bundle price
        // rather than the component subtotal. The saving is zero for a visit
        // that was not booked as a bundle, which leaves that arithmetic alone.
        var discountable = invoice.SubTotal - invoice.BundleDiscount;

        var couponCheck = await CheckCouponAsync(request.CouponCode, discountable, errors, cancellationToken);
        var manualCheck = ValidateManualDiscount(request, discountable, errors);

        // The combined cap is only meaningful once each part passed on its own;
        // a part that is already flagged would only produce a second, misleading
        // message here.
        if (!couponCheck.Flagged && !manualCheck.Flagged
            && couponCheck.Discount + manualCheck.Discount > discountable)
        {
            errors.Add("manualDiscountValue", "Discounts cannot exceed the subtotal.");
        }

        var total = discountable - couponCheck.Discount - manualCheck.Discount;

        // The amount is only checked once the discounts themselves are valid;
        // comparing against a total that is about to be rejected would just add
        // a misleading second message.
        if (errors.IsValid)
        {
            ValidateAmount(request.Amount, total, invoice.Currency, errors);
        }

        var method = ValidatePaymentMethod(request.PaymentMethod, errors);
        var reference = ValidateReference(request.Reference, errors);

        errors.ThrowIfInvalid();

        var now = timeProvider.GetUtcNow();

        invoice.CouponId = couponCheck.Coupon?.Id;
        invoice.CouponCode = couponCheck.Coupon?.Code;
        invoice.CouponDiscount = couponCheck.Discount;
        invoice.ManualDiscount = manualCheck.Discount;
        invoice.DiscountTotal = invoice.BundleDiscount + couponCheck.Discount + manualCheck.Discount;
        invoice.TotalAmount = total;
        invoice.IssuedAt = now;
        invoice.Status = InvoiceStatus.Paid;

        // One tender per invoice in Phase 1: the recorded payment is the whole
        // total. Split payments arrive later by adding more Payment rows.
        invoice.Payments.Add(new Payment
        {
            InvoiceId = invoice.Id,
            Amount = total,
            Currency = invoice.Currency,
            Method = method,
            Status = PaymentStatus.Completed,
            Reference = reference,
            PaidAt = now
        });

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapReceipt(invoice);
    }

    public async Task<ReceiptDto> GetReceiptAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        if (CompletedPayment(invoice) is null)
        {
            throw new ConflictException($"Invoice '{invoice.InvoiceNumber}' has not been paid yet.");
        }

        return MapReceipt(invoice);
    }

    private async Task<CouponCheck> CheckCouponAsync(
        string? code,
        decimal subtotal,
        ValidationErrorBuilder errors,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            return new CouponCheck(null, 0m, Flagged: false);
        }

        // Matching is on the stored form — trimmed and upper-cased — so the
        // code works however it was typed and pasted.
        var normalized = code.Trim().ToUpperInvariant();
        var coupon = await coupons.GetByCodeAsync(normalized, cancellationToken);

        var today = DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime);

        if (coupon is null || !CouponRules.IsApplicable(coupon, today))
        {
            errors.Add("couponCode", "Coupon code is invalid or expired.");

            return new CouponCheck(null, 0m, Flagged: true);
        }

        var discount = DiscountMath.Compute(subtotal, coupon.DiscountType, coupon.DiscountValue);

        if (discount > subtotal)
        {
            errors.Add("couponCode", "Discount cannot exceed the subtotal.");

            return new CouponCheck(coupon, discount, Flagged: true);
        }

        return new CouponCheck(coupon, discount, Flagged: false);
    }

    private static ManualCheck ValidateManualDiscount(
        CheckoutRequest request,
        decimal subtotal,
        ValidationErrorBuilder errors)
    {
        if (request.ManualDiscountType is null && request.ManualDiscountValue is null)
        {
            return new ManualCheck(null, 0m, Flagged: false);
        }

        var flagged = false;
        DiscountType? type = null;

        if (string.IsNullOrWhiteSpace(request.ManualDiscountType)
            || !Enum.TryParse<DiscountType>(request.ManualDiscountType.Trim(), ignoreCase: true, out var parsed)
            || !Enum.IsDefined(parsed))
        {
            errors.Add("manualDiscountType", "Select Fixed or Percentage.");
            flagged = true;
        }
        else
        {
            type = parsed;
        }

        var value = request.ManualDiscountValue;

        if (value is null)
        {
            errors.Add("manualDiscountValue", "Enter a discount value.");
            flagged = true;
        }
        else if (type == DiscountType.Percentage && (value <= 0m || value > 100m))
        {
            errors.Add("manualDiscountValue", "Enter a percentage between 0 and 100.");
            flagged = true;
        }
        else if (type == DiscountType.Fixed && value <= 0m)
        {
            errors.Add("manualDiscountValue", "Enter a discount amount greater than zero.");
            flagged = true;
        }
        else if (value < 0m)
        {
            errors.Add("manualDiscountValue", "Enter a discount amount greater than zero.");
            flagged = true;
        }

        if (flagged || type is not { } discountType || value is not { } discountValue)
        {
            return new ManualCheck(type, 0m, Flagged: true);
        }

        var discount = DiscountMath.Compute(subtotal, discountType, discountValue);

        if (discount > subtotal)
        {
            errors.Add("manualDiscountValue", "Discount cannot exceed the subtotal.");

            return new ManualCheck(type, discount, Flagged: true);
        }

        return new ManualCheck(type, discount, Flagged: false);
    }

    private static void ValidateAmount(
        decimal? amount,
        decimal total,
        string currency,
        ValidationErrorBuilder errors)
    {
        if (amount is null)
        {
            errors.Add("amount", "Enter the amount collected.");

            return;
        }

        if (amount.Value != total)
        {
            // The total is always rounded to fils, so the message shows the
            // exact figure the receptionist was asked to collect.
            var formatted = total.ToString("0.00", CultureInfo.InvariantCulture);

            errors.Add("amount", $"Payment amount must equal the invoice total of {currency} {formatted}.");
        }
    }

    private static PaymentMethod ValidatePaymentMethod(string? method, ValidationErrorBuilder errors)
    {
        if (!string.IsNullOrWhiteSpace(method)
            && Enum.TryParse<PaymentMethod>(method.Trim(), ignoreCase: true, out var parsed)
            && PhaseOnePaymentMethods.Contains(parsed))
        {
            return parsed;
        }

        errors.Add("paymentMethod", "Select a payment method (Cash, Card or Bank Transfer).");

        return PaymentMethod.Cash;
    }

    private static string? ValidateReference(string? reference, ValidationErrorBuilder errors)
    {
        var trimmed = reference?.Trim();

        if (string.IsNullOrEmpty(trimmed))
        {
            return null;
        }

        if (trimmed.Length > BillingLimits.PaymentReferenceMaxLength)
        {
            errors.Add("reference", $"Reference must be {BillingLimits.PaymentReferenceMaxLength} characters or fewer.");

            return null;
        }

        return trimmed;
    }

    private async Task<string> NextInvoiceNumberAsync(int year, CancellationToken cancellationToken)
    {
        var highest = await invoices.GetHighestSequenceAsync(year, cancellationToken);

        return $"{BillingLimits.InvoiceNumberPrefix}-{year:D4}-{highest + 1:D4}";
    }

    private async Task<Invoice> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await invoices.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Invoice '{id}' was not found.");
    }

    private static Payment? CompletedPayment(Invoice invoice) =>
        invoice.Payments
            .Where(payment => payment.Status == PaymentStatus.Completed)
            .OrderByDescending(payment => payment.PaidAt)
            .FirstOrDefault();

    private static InvoiceSummaryDto MapSummary(Invoice invoice)
    {
        var payment = CompletedPayment(invoice);

        return new InvoiceSummaryDto(
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.CustomerId,
            DisplayName(invoice),
            invoice.AppointmentId,
            invoice.Status.ToString(),
            invoice.Currency,
            invoice.SubTotal,
            invoice.DiscountTotal,
            invoice.TotalAmount,
            payment?.Method.ToString(),
            invoice.IssuedAt,
            invoice.CreatedAt);
    }

    private static InvoiceDto Map(Invoice invoice) => new(
        invoice.Id,
        invoice.InvoiceNumber,
        invoice.Status.ToString(),
        invoice.Currency,
        invoice.AppointmentId,
        invoice.CustomerId,
        invoice.Customer is { } customer ? $"{customer.FirstName} {customer.LastName}" : null,
        invoice.Appointment?.WalkInCustomerName,
        invoice.CouponId,
        invoice.CouponCode,
        invoice.ServiceBundleId,
        invoice.BundleNameSnapshot,
        invoice.SubTotal,
        invoice.BundleDiscount,
        invoice.CouponDiscount,
        invoice.ManualDiscount,
        invoice.DiscountTotal,
        invoice.TotalAmount,
        invoice.IssuedAt,
        invoice.CreatedAt,
        invoice.UpdatedAt,
        [.. invoice.Items
            .OrderBy(item => item.SortOrder)
            .ThenBy(item => item.CreatedAt)
            .Select(item => new InvoiceItemDto(
                item.Id,
                item.Description,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal,
                item.ServiceId,
                item.AppointmentServiceId,
                item.SortOrder))],
        [.. invoice.Payments
            .OrderBy(payment => payment.PaidAt)
            .ThenBy(payment => payment.Id)
            .Select(payment => new InvoicePaymentDto(
                payment.Id,
                payment.Amount,
                payment.Currency,
                payment.Method.ToString(),
                payment.Status.ToString(),
                payment.Reference,
                payment.PaidAt))]);

    private static ReceiptDto MapReceipt(Invoice invoice)
    {
        var payment = CompletedPayment(invoice)
            ?? throw new InvalidOperationException($"Invoice '{invoice.Id}' has no completed payment.");

        return new ReceiptDto(
            BillingLimits.SalonName,
            invoice.Id,
            invoice.InvoiceNumber,
            DisplayName(invoice),
            [.. invoice.Items
                .OrderBy(item => item.SortOrder)
                .ThenBy(item => item.CreatedAt)
                .Select(item => new ReceiptLineDto(
                    item.Description,
                    item.Quantity,
                    item.UnitPrice,
                    item.LineTotal))],
            invoice.SubTotal,
            invoice.BundleNameSnapshot,
            invoice.BundleDiscount,
            invoice.CouponCode,
            invoice.CouponDiscount,
            invoice.ManualDiscount,
            invoice.TotalAmount,
            invoice.Currency,
            payment.Method.ToString(),
            payment.Reference,
            payment.PaidAt,
            invoice.IssuedAt);
    }

    private static string? DisplayName(Invoice invoice) =>
        invoice.Customer is { } customer
            ? $"{customer.FirstName} {customer.LastName}"
            : invoice.Appointment?.WalkInCustomerName
              ?? invoice.Notes;

    /// <summary>A coupon check result: the resolved coupon, its computed discount, and whether it failed validation.</summary>
    private sealed record CouponCheck(Coupon? Coupon, decimal Discount, bool Flagged);

    /// <summary>A manual discount check result: the resolved type, its computed discount, and whether it failed validation.</summary>
    private sealed record ManualCheck(DiscountType? Type, decimal Discount, bool Flagged);
}
