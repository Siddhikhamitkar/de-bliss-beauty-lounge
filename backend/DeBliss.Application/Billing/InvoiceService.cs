using System.Globalization;
using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Billing.Ports;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Inventory.Ports;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Inventory;
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
    ICustomerRepository customers,
    IInventoryRepository inventory,
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
        // A retail sale that is not tied to a booking: an empty draft the
        // receptionist adds product lines to. There is no appointment to
        // resume, so nothing here can collide with an open bill.
        if (request.AppointmentId is null)
        {
            var retail = await StartRetailAsync(request, cancellationToken);

            return new InvoiceStartResult(Map(retail), WasCreated: true);
        }

        var appointment = await appointments.GetByIdAsync(request.AppointmentId.Value, cancellationToken);

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

    public async Task<InvoiceDto> AddLineAsync(
        Guid id,
        AddInvoiceLineRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        AssertDraft(invoice);

        var errors = new ValidationErrorBuilder();

        if (request.ProductId is null)
        {
            errors.Add("productId", "Select a product.");

            errors.ThrowIfInvalid();

            return Map(invoice);
        }

        var product = await inventory.GetByIdAsync(request.ProductId.Value, cancellationToken);

        if (product is null)
        {
            errors.Add("productId", "Select a product.");
        }
        else if (!product.IsActive)
        {
            errors.Add("productId", $"'{product.Name}' is no longer for sale.");
        }
        else if (product.InventoryType != InventoryType.Retail)
        {
            errors.Add("productId", $"'{product.Name}' is a salon-use product and cannot be sold.");
        }
        else if (product.SellingPrice is null)
        {
            errors.Add("productId", $"'{product.Name}' has no selling price.");
        }

        var quantity = request.Quantity;

        if (quantity is null)
        {
            errors.Add("quantity", "Enter the quantity.");
        }
        else if (quantity.Value <= 0m)
        {
            errors.Add("quantity", "Quantity must be greater than zero.");
        }
        else if (product is not null && product.CurrentStock < quantity.Value)
        {
            // An early, readable rejection; checkout re-checks against live
            // stock, which is where overselling is actually prevented.
            errors.Add(
                "quantity",
                $"Only {product.CurrentStock} {product.UnitOfMeasure} of '{product.Name}' in stock.");
        }

        errors.ThrowIfInvalid();

        // One line per product: buying two more of what is already on the bill
        // raises that line's quantity rather than stacking a near-duplicate.
        var existingLine = invoice.Items.FirstOrDefault(line => line.ProductId == product!.Id);

        if (existingLine is null)
        {
            invoice.Items.Add(new InvoiceItem
            {
                InvoiceId = invoice.Id,
                ProductId = product!.Id,
                ItemType = InvoiceItemType.Product,
                Description = ProductDescription(product),
                Quantity = quantity!.Value,
                UnitPrice = product.SellingPrice!.Value,
                LineTotal = LineTotalFor(quantity!.Value, product.SellingPrice!.Value),
                SortOrder = invoice.Items.Max(line => (int?)line.SortOrder) is { } highest ? highest + 1 : 1
            });
        }
        else
        {
            existingLine.Quantity += quantity!.Value;
            existingLine.LineTotal = LineTotalFor(existingLine.Quantity, existingLine.UnitPrice);
        }

        RecalculateDraftTotals(invoice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(invoice);
    }

    public async Task<InvoiceDto> UpdateLineAsync(
        Guid id,
        Guid lineId,
        UpdateInvoiceLineRequest request,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        AssertDraft(invoice);

        var line = LineForEdit(invoice, lineId);

        var errors = new ValidationErrorBuilder();

        var quantity = request.Quantity;

        if (quantity is null)
        {
            errors.Add("quantity", "Enter the quantity.");
        }
        else if (quantity.Value <= 0m)
        {
            errors.Add("quantity", "Quantity must be greater than zero.");
        }

        errors.ThrowIfInvalid();

        line.Quantity = quantity!.Value;
        line.LineTotal = LineTotalFor(line.Quantity, line.UnitPrice);

        RecalculateDraftTotals(invoice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(invoice);
    }

    public async Task<InvoiceDto> RemoveLineAsync(
        Guid id,
        Guid lineId,
        CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(id, cancellationToken);

        AssertDraft(invoice);

        var line = LineForEdit(invoice, lineId);

        invoice.Items.Remove(line);

        RecalculateDraftTotals(invoice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(invoice);
    }

    public async Task<ReceiptDto> CheckoutAsync(
        Guid id,
        CheckoutRequest request,
        Guid? actorUserId = null,
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

        // Products on the bill are re-validated against live stock: the picker
        // offered what was on the shelf when the line was added, and checkout
        // is the moment the units actually leave. Loading the products tracked
        // here is also what arms the concurrency token that stops two bills
        // selling the same last unit.
        var productLines = invoice.Items
            .Where(item => item.ProductId is not null)
            .ToList();

        var stocked = new Dictionary<Guid, InventoryProduct>();

        if (invoice.Items.Count == 0)
        {
            errors.Add("items", "An invoice needs at least one line before checkout.");
        }

        if (productLines.Count > 0)
        {
            stocked = (await inventory.GetTrackedByIdAsync(
                [.. productLines.Select(item => item.ProductId!.Value)], cancellationToken))
                .ToDictionary(product => product.Id);

            foreach (var line in productLines)
            {
                if (!stocked.TryGetValue(line.ProductId!.Value, out var product))
                {
                    errors.Add("items", $"'{line.Description}' is no longer available.");
                }
                else if (!product.IsActive || product.InventoryType != InventoryType.Retail)
                {
                    errors.Add("items", $"'{product.Name}' is no longer for sale.");
                }
                else if (product.CurrentStock < line.Quantity)
                {
                    errors.Add(
                        "items",
                        $"Only {product.CurrentStock} {product.UnitOfMeasure} of '{product.Name}' in stock.");
                }
            }
        }

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

        // The sold units leave stock in the same save as the payment: either
        // the whole checkout lands — bill paid, stock down, movements written —
        // or none of it does. A retried checkout of the now-paid invoice is
        // rejected above, so the same units can never leave twice.
        foreach (var line in productLines)
        {
            var product = stocked[line.ProductId!.Value];
            var before = product.CurrentStock;

            product.ApplyStockChange(-line.Quantity);

            inventory.AddMovement(new StockMovement
            {
                ProductId = product.Id,
                MovementType = StockMovementType.RetailSale,
                QuantityChange = -line.Quantity,
                StockBefore = before,
                StockAfter = product.CurrentStock,
                OccurredAt = now,
                InvoiceId = invoice.Id,
                PerformedByUserId = actorUserId
            });
        }

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

    private async Task<Invoice> StartRetailAsync(
        CreateInvoiceRequest request,
        CancellationToken cancellationToken)
    {
        Customer? customer = null;

        if (request.CustomerId is { } customerId)
        {
            customer = await customers.GetByIdAsync(customerId, cancellationToken)
                ?? throw new NotFoundException($"Customer '{customerId}' was not found.");
        }

        var now = timeProvider.GetUtcNow();

        var invoice = new Invoice
        {
            InvoiceNumber = await NextInvoiceNumberAsync(now.Year, cancellationToken),
            CustomerId = customer?.Id,
            Customer = customer,
            Status = InvoiceStatus.Draft,
            Currency = BillingLimits.CurrencyCode,
            Notes = request.WalkInCustomerName?.Trim() is { Length: > 0 } walkIn ? walkIn : null
        };

        invoices.Add(invoice);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return invoice;
    }

    private static void AssertDraft(Invoice invoice)
    {
        if (invoice.Status == InvoiceStatus.Paid)
        {
            throw new ConflictException($"Invoice '{invoice.InvoiceNumber}' has already been paid.");
        }

        if (invoice.Status != InvoiceStatus.Draft)
        {
            throw new ConflictException($"Invoice '{invoice.InvoiceNumber}' is not awaiting checkout.");
        }
    }

    /// <summary>
    /// The line a line-edit action targets. Only product lines are editable:
    /// service lines are snapshots of what was booked and are never re-keyed
    /// by hand.
    /// </summary>
    private static InvoiceItem LineForEdit(Invoice invoice, Guid lineId)
    {
        var line = invoice.Items.FirstOrDefault(item => item.Id == lineId)
            ?? throw new NotFoundException($"Invoice line '{lineId}' was not found.");

        if (line.ProductId is null)
        {
            throw new ConflictException("Service lines come from the booking and cannot be edited.");
        }

        return line;
    }

    /// <summary>
    /// Keeps the draft's arithmetic honest while it is being built: subtotal is
    /// the sum of the lines, and the total still respects any bundle saving,
    /// exactly as StartFromAppointmentAsync left them.
    /// </summary>
    private static void RecalculateDraftTotals(Invoice invoice)
    {
        invoice.SubTotal = invoice.Items.Sum(item => item.LineTotal);
        invoice.TotalAmount = invoice.SubTotal - invoice.BundleDiscount;
        invoice.DiscountTotal = invoice.BundleDiscount;
    }

    private static decimal LineTotalFor(decimal quantity, decimal unitPrice)
    {
        return decimal.Round(quantity * unitPrice, 2, MidpointRounding.AwayFromZero);
    }

    private static string ProductDescription(InventoryProduct product)
    {
        return product.Brand is { Length: > 0 } brand ? $"{brand} {product.Name}" : product.Name;
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
        DisplayName(invoice),
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
                item.ProductId,
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
