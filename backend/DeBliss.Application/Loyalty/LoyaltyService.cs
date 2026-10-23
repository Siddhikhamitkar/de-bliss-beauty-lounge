using DeBliss.Application.Billing;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Loyalty.Dtos;
using DeBliss.Application.Loyalty.Ports;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Loyalty;

namespace DeBliss.Application.Loyalty;

public sealed class LoyaltyService(
    ILoyaltyRepository loyalty,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ILoyaltyService
{
    private const string RedemptionField = "loyaltyPointsToRedeem";
    private const decimal MaximumStoredAmount = 9_999_999_999_999_999.99m;

    public async Task<LoyaltySettingsDto> GetSettingsAsync(CancellationToken ct = default) =>
        MapSettings(await loyalty.GetSettingsAsync(ct: ct) ?? new LoyaltySettings());

    public async Task<LoyaltySettingsDto> UpdateSettingsAsync(
        SaveLoyaltySettingsRequest request, Guid? actorUserId = null, CancellationToken ct = default)
    {
        var errors = new ValidationErrorBuilder();
        if (request.IsEnabled is null)
            errors.Add("isEnabled", "Choose whether loyalty is enabled.");
        if (request.AllowPartialRedemption is null)
            errors.Add("allowPartialRedemption", "Choose whether partial redemption is allowed.");
        ValidateNumber(errors, "earningPercentage", request.EarningPercentage, 0m, 100m, 2);
        ValidateNumber(errors, "pointValueAed", request.PointValueAed, 0.0001m, 1000m, 4);
        ValidateNumber(errors, "minimumRedemptionPoints", request.MinimumRedemptionPoints, 0m, 1_000_000_000m, 2);
        ValidateNumber(errors, "maximumRedemptionPercentage", request.MaximumRedemptionPercentage, 0m, 100m, 2);
        if (request.MinimumInvoiceAmount is not null)
            ValidateNumber(errors, "minimumInvoiceAmount", request.MinimumInvoiceAmount, 0m, 1_000_000_000m, 2);
        errors.ThrowIfInvalid();

        var current = await loyalty.GetSettingsAsync(tracked: true, ct: ct);
        if (current is null)
        {
            current = new LoyaltySettings();
            loyalty.AddSettings(current);
        }

        current.IsEnabled = request.IsEnabled!.Value;
        current.EarningPercentage = request.EarningPercentage!.Value;
        current.PointValueAed = request.PointValueAed!.Value;
        current.MinimumRedemptionPoints = request.MinimumRedemptionPoints!.Value;
        current.MaximumRedemptionPercentage = request.MaximumRedemptionPercentage!.Value;
        current.AllowPartialRedemption = request.AllowPartialRedemption!.Value;
        current.MinimumInvoiceAmount = request.MinimumInvoiceAmount;
        current.UpdatedByUserId = actorUserId;
        current.Version = Guid.NewGuid();
        await unitOfWork.SaveChangesAsync(ct);
        return MapSettings(current);
    }

    public async Task<CustomerLoyaltySummaryDto> GetSummaryAsync(Guid customerId, CancellationToken ct = default)
    {
        await RequireCustomerAsync(customerId, ct);
        var totals = await loyalty.GetTotalsAsync(customerId, ct);
        var recent = await loyalty.GetRecentTransactionsAsync(customerId, 10, ct);
        return new CustomerLoyaltySummaryDto(customerId, Math.Max(0m, totals.Balance),
            totals.LifetimePointsEarned, totals.PointsRedeemed, Math.Max(0m, -totals.Balance), recent);
    }

    public async Task<PagedResult<LoyaltyTransactionDto>> ListTransactionsAsync(
        Guid customerId, int page = 1, int pageSize = 50, CancellationToken ct = default)
    {
        await RequireCustomerAsync(customerId, ct);
        pageSize = pageSize <= 0 ? 50 : Math.Min(pageSize, 200);
        page = Math.Clamp(page, 1, int.MaxValue / pageSize);
        var (items, totalCount) = await loyalty.ListTransactionsAsync(customerId, page, pageSize, ct);
        return new PagedResult<LoyaltyTransactionDto>(items, totalCount, page, pageSize);
    }

    public async Task<LoyaltyCalculation> CalculateAsync(
        Guid? customerId, decimal merchandiseAmount, decimal taxRate, bool pricesIncludeTax,
        decimal pointsToRedeem, CancellationToken ct = default)
    {
        var errors = new ValidationErrorBuilder();
        ValidateNumber(errors, RedemptionField, pointsToRedeem, 0m, MaximumStoredAmount, 2);
        ValidateNumber(errors, "merchandiseAmount", merchandiseAmount, 0m, MaximumStoredAmount, 2);
        ValidateNumber(errors, "taxRate", taxRate, 0m, 100m, 2);
        errors.ThrowIfInvalid();

        if (customerId is { } id)
            await RequireCustomerAsync(id, ct);

        var settings = await GetSettingsAsync(ct);
        var balance = customerId is { } customer
            ? (await loyalty.GetTotalsAsync(customer, ct)).Balance
            : 0m;
        var availablePoints = Math.Max(0m, balance);
        var preLoyaltySpend = EligibleSpend(merchandiseAmount, taxRate, pricesIncludeTax);
        var cap = preLoyaltySpend * settings.MaximumRedemptionPercentage / 100m;
        var maximumPoints = Floor2(Math.Min(availablePoints, cap / settings.PointValueAed));
        string? unavailableReason = null;

        if (!settings.IsEnabled)
            unavailableReason = "Loyalty is disabled.";
        else if (customerId is null)
            unavailableReason = "Select a customer to use loyalty points.";
        else if (settings.MinimumInvoiceAmount is { } minimum && preLoyaltySpend < minimum)
            unavailableReason = "The invoice is below the minimum eligible amount for redemption.";
        else if (availablePoints <= 0m)
            unavailableReason = "No loyalty points are available.";
        else if (!settings.AllowPartialRedemption && maximumPoints < availablePoints)
            unavailableReason = "The full points balance exceeds the invoice redemption limit.";
        else if (maximumPoints < settings.MinimumRedemptionPoints)
            unavailableReason = "The redeemable points are below the minimum redemption points.";
        else if (Floor2(maximumPoints * settings.PointValueAed) < 0.01m)
            unavailableReason = "The redeemable points are worth less than AED 0.01.";

        if (unavailableReason is not null)
            maximumPoints = 0m;

        if (pointsToRedeem > 0m)
        {
            if (unavailableReason is not null)
                errors.Add(RedemptionField, unavailableReason);
            else if (pointsToRedeem > maximumPoints)
                errors.Add(RedemptionField, "The requested points exceed the available balance or invoice redemption limit.");
            else if (pointsToRedeem < settings.MinimumRedemptionPoints)
                errors.Add(RedemptionField, "The requested points are below the minimum redemption points.");
            else if (!settings.AllowPartialRedemption && pointsToRedeem != availablePoints)
                errors.Add(RedemptionField, "Redeem the full points balance; partial redemption is disabled.");
            else if (Floor2(pointsToRedeem * settings.PointValueAed) < 0.01m)
                errors.Add(RedemptionField, "The requested points must be worth at least AED 0.01.");
        }
        errors.ThrowIfInvalid();

        var discount = Floor2(pointsToRedeem * settings.PointValueAed);
        // Inclusive prices lose the sticker-price discount before their VAT is extracted.
        var eligibleSpend = EligibleSpend(merchandiseAmount - discount, taxRate, pricesIncludeTax);
        var earned = settings.IsEnabled && customerId is not null
            ? decimal.Round(eligibleSpend * settings.EarningPercentage / 100m,
                2, MidpointRounding.AwayFromZero)
            : 0m;

        var quote = new LoyaltyQuoteDto(settings.IsEnabled, availablePoints,
            Floor2(availablePoints * settings.PointValueAed), settings.PointValueAed,
            maximumPoints, Floor2(maximumPoints * settings.PointValueAed),
            settings.MinimumRedemptionPoints, settings.MaximumRedemptionPercentage,
            settings.AllowPartialRedemption, settings.MinimumInvoiceAmount, unavailableReason,
            pointsToRedeem, discount, eligibleSpend, earned);
        return new LoyaltyCalculation(quote, settings) { CustomerId = customerId };
    }

    public void RecordCheckout(Invoice invoice, LoyaltyCalculation calculation, Guid? actorUserId)
    {
        if (!calculation.Settings.IsEnabled || invoice.CustomerId is null || invoice.Status != InvoiceStatus.Paid ||
            !invoice.Payments.Any(payment => payment.Status == PaymentStatus.Completed &&
                payment.DeletedAt is null && payment.RefundOfPaymentId is null && payment.Amount >= 0m))
            return;

        if (invoice.CustomerId != calculation.CustomerId)
            throw new ConflictException("The loyalty calculation belongs to a different customer. Recalculate before checkout.");

        var occurredAt = timeProvider.GetUtcNow();
        if (calculation.Quote.PointsToRedeem > 0m)
            AddEntry(LoyaltyTransactionType.Redeem, -calculation.Quote.PointsToRedeem, "redeem");
        if (calculation.Quote.PointsEarned > 0m)
            AddEntry(LoyaltyTransactionType.Earn, calculation.Quote.PointsEarned, "earn");

        void AddEntry(LoyaltyTransactionType type, decimal points, string keySuffix) =>
            loyalty.AddTransaction(new CustomerLoyaltyTransaction
            {
                Type = type,
                CustomerId = invoice.CustomerId.Value,
                InvoiceId = invoice.Id,
                Points = points,
                OccurredAt = occurredAt,
                Description = type == LoyaltyTransactionType.Earn
                    ? $"Points earned on invoice {invoice.InvoiceNumber}."
                    : $"Points redeemed on invoice {invoice.InvoiceNumber}.",
                EligibleSpend = calculation.Quote.EligibleSpend,
                EarningPercentage = calculation.Settings.EarningPercentage,
                PointValueAed = calculation.Settings.PointValueAed,
                CreatedByUserId = actorUserId,
                IdempotencyKey = $"invoice:{invoice.Id:D}:{keySuffix}"
            });
    }

    private async Task RequireCustomerAsync(Guid customerId, CancellationToken ct)
    {
        if (await customers.GetByIdAsync(customerId, ct) is null)
            throw new NotFoundException($"Customer '{customerId}' was not found.");
    }

    private static decimal EligibleSpend(decimal merchandiseAmount, decimal taxRate, bool inclusive) =>
        inclusive ? merchandiseAmount - TaxMath.Compute(merchandiseAmount, taxRate, true) : merchandiseAmount;

    private static decimal Floor2(decimal value) => decimal.Round(value, 2, MidpointRounding.ToZero);

    private static void ValidateNumber(
        ValidationErrorBuilder errors, string field, decimal? value, decimal minimum, decimal maximum, int precision)
    {
        if (value is null)
            errors.Add(field, "Enter a value.");
        else if (value < minimum || value > maximum)
            errors.Add(field, $"Enter a value between {minimum} and {maximum}.");
        else if (decimal.Round(value.Value, precision) != value.Value)
            errors.Add(field, $"Enter a value with no more than {precision} decimal places.");
    }

    private static LoyaltySettingsDto MapSettings(LoyaltySettings settings) => new(
        settings.IsEnabled, settings.EarningPercentage, settings.PointValueAed,
        settings.MinimumRedemptionPoints, settings.MaximumRedemptionPercentage,
        settings.AllowPartialRedemption, settings.MinimumInvoiceAmount);
}
