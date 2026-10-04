using DeBliss.Application.Billing.Dtos;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Billing;

public class CouponServiceTests
{
    private static SaveCouponRequest Request(
        string? code = "BLISS10",
        string? discountType = "Fixed",
        decimal? discountValue = 10m,
        DateOnly? validFrom = null,
        DateOnly? validUntil = null,
        bool? isActive = null) =>
        new(code, discountType, discountValue, validFrom, validUntil, isActive);

    [Fact]
    public async Task Creating_a_coupon_normalises_the_code_and_defaults_to_active()
    {
        using var harness = new BillingHarness();

        var created = await harness.Coupons.CreateAsync(Request(code: " bliss10 "));

        Assert.Equal("BLISS10", created.Code);
        Assert.Equal("Fixed", created.DiscountType);
        Assert.Equal(10m, created.DiscountValue);
        Assert.True(created.IsActive);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);

        await using var verify = harness.Reopen();

        Assert.Equal("BLISS10", (await verify.Coupons.SingleAsync()).Code);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Creating_a_coupon_without_a_code_is_rejected(string? code)
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(code: code)));

        Assert.Equal("Coupon code is required.", Assert.Single(failure.Errors["code"]));
    }

    [Fact]
    public async Task Creating_a_coupon_with_an_overlong_code_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(code: new string('X', 51))));

        Assert.Equal(
            "Coupon code must be 50 characters or fewer.",
            Assert.Single(failure.Errors["code"]));
    }

    [Fact]
    public async Task Creating_a_coupon_with_a_duplicate_code_is_rejected()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("BLISS10");

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Coupons.CreateAsync(Request(code: "bliss10")));

        Assert.Equal("A coupon with the code 'BLISS10' already exists.", failure.Message);
    }

    [Fact]
    public async Task Creating_a_coupon_with_an_unknown_discount_type_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(discountType: "BuyOneGetOne")));

        Assert.Equal("Select Fixed or Percentage.", Assert.Single(failure.Errors["discountType"]));
    }

    [Fact]
    public async Task Creating_a_percentage_coupon_above_one_hundred_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(discountType: "Percentage", discountValue: 150m)));

        Assert.Equal(
            "Enter a percentage between 0 and 100.",
            Assert.Single(failure.Errors["discountValue"]));
    }

    [Fact]
    public async Task Creating_a_fixed_coupon_with_a_non_positive_value_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(discountValue: 0m)));

        Assert.Equal(
            "Enter a discount amount greater than zero.",
            Assert.Single(failure.Errors["discountValue"]));
    }

    [Fact]
    public async Task Creating_a_coupon_with_valid_until_before_valid_from_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.CreateAsync(Request(
                validFrom: new DateOnly(2026, 10, 10),
                validUntil: new DateOnly(2026, 10, 1))));

        Assert.Equal(
            "Valid until must be on or after valid from.",
            Assert.Single(failure.Errors["validUntil"]));
    }

    [Fact]
    public async Task Updating_a_coupon_changes_its_details_and_keeps_its_status()
    {
        using var harness = new BillingHarness();
        var created = await harness.Coupons.CreateAsync(Request(isActive: false));

        var updated = await harness.Coupons.UpdateAsync(
            created.Id, Request(discountType: "Percentage", discountValue: 15m));

        Assert.Equal("BLISS10", updated.Code);
        Assert.Equal("Percentage", updated.DiscountType);
        Assert.Equal(15m, updated.DiscountValue);
        Assert.False(updated.IsActive);

        var reactivated = await harness.Coupons.UpdateAsync(created.Id, Request(isActive: true));

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task Deactivating_and_reactivating_a_coupon_persists()
    {
        using var harness = new BillingHarness();
        var created = await harness.Coupons.CreateAsync(Request());

        var deactivated = await harness.Coupons.SetStatusAsync(created.Id, isActive: false);

        Assert.False(deactivated.IsActive);

        await using (var verify = harness.Reopen())
        {
            Assert.False((await verify.Coupons.SingleAsync()).IsActive);
        }

        var reactivated = await harness.Coupons.SetStatusAsync(created.Id, isActive: true);

        Assert.True(reactivated.IsActive);

        await using (var verify = harness.Reopen())
        {
            Assert.True((await verify.Coupons.SingleAsync()).IsActive);
        }
    }

    [Fact]
    public async Task Getting_an_unknown_coupon_is_not_found()
    {
        using var harness = new BillingHarness();
        var missingId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Coupons.GetAsync(missingId));

        Assert.Contains(missingId.ToString(), failure.Message);
    }

    [Fact]
    public async Task Searching_filters_by_term_and_status()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("BLISS10");
        await harness.SeedCouponAsync("BLISS20", isActive: false);
        await harness.SeedCouponAsync("SUMMER5");

        var all = await harness.Coupons.SearchAsync(new CouponQuery("bliss", CouponStatusFilter.All, 1, 50));
        var inactive = await harness.Coupons.SearchAsync(new CouponQuery(null, CouponStatusFilter.Inactive, 1, 50));
        var active = await harness.Coupons.SearchAsync(new CouponQuery(null, CouponStatusFilter.Active, 1, 50));

        Assert.Equal("BLISS10, BLISS20", string.Join(", ", all.Items.Select(coupon => coupon.Code)));
        Assert.Equal(2, all.TotalCount);
        Assert.Equal("BLISS20", Assert.Single(inactive.Items).Code);
        Assert.Equal(
            "BLISS10, SUMMER5",
            string.Join(", ", active.Items.Select(coupon => coupon.Code).Order()));
    }

    [Fact]
    public async Task Validating_a_fixed_coupon_returns_the_discount_amount()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("BLISS50", discountValue: 50m);

        var result = await harness.Coupons.ValidateAsync(new ValidateCouponRequest(" bliss50 ", 160m));

        Assert.Equal("BLISS50", result.Code);
        Assert.Equal("Fixed", result.DiscountType);
        Assert.Equal(50m, result.DiscountValue);
        Assert.Equal(50m, result.DiscountAmount);
    }

    [Fact]
    public async Task Validating_a_percentage_coupon_rounds_to_fils()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("TENOFF", DiscountType.Percentage, 10m);

        // 10% of 159.99 is 15.999, rounded to 16.00 away from zero.
        var result = await harness.Coupons.ValidateAsync(new ValidateCouponRequest("TENOFF", 159.99m));

        Assert.Equal(16m, result.DiscountAmount);
    }

    [Fact]
    public async Task A_coupon_is_valid_through_its_last_day()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync(
            "TODAY",
            validFrom: new DateOnly(2026, 10, 3),
            validUntil: new DateOnly(2026, 10, 3));

        var result = await harness.Coupons.ValidateAsync(new ValidateCouponRequest("TODAY", 160m));

        Assert.Equal(10m, result.DiscountAmount);
    }

    [Theory]
    [InlineData("NOPE")]
    [InlineData("SWITCHEDOFF")]
    [InlineData("EXPIRED")]
    [InlineData("NOTYET")]
    public async Task Validating_a_coupon_that_is_not_applicable_is_rejected_generically(string code)
    {
        using var harness = new BillingHarness();

        switch (code)
        {
            case "SWITCHEDOFF":
                await harness.SeedCouponAsync("SWITCHEDOFF", isActive: false);
                break;
            case "EXPIRED":
                await harness.SeedCouponAsync("EXPIRED", validUntil: new DateOnly(2026, 10, 2));
                break;
            case "NOTYET":
                await harness.SeedCouponAsync("NOTYET", validFrom: new DateOnly(2026, 10, 4));
                break;
        }

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.ValidateAsync(new ValidateCouponRequest(code, 160m)));

        Assert.Equal(
            "Coupon code is invalid or expired.",
            Assert.Single(failure.Errors["code"]));
    }

    [Fact]
    public async Task Validating_a_blank_code_is_rejected_generically()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.ValidateAsync(new ValidateCouponRequest("   ", 160m)));

        Assert.Equal(
            "Coupon code is invalid or expired.",
            Assert.Single(failure.Errors["code"]));
    }

    [Fact]
    public async Task Validating_a_coupon_above_the_subtotal_is_rejected()
    {
        using var harness = new BillingHarness();
        await harness.SeedCouponAsync("HUGE", discountValue: 200m);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.ValidateAsync(new ValidateCouponRequest("HUGE", 160m)));

        Assert.Equal(
            "Discount cannot exceed the subtotal.",
            Assert.Single(failure.Errors["code"]));
    }

    [Fact]
    public async Task Validating_a_negative_subtotal_is_rejected()
    {
        using var harness = new BillingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Coupons.ValidateAsync(new ValidateCouponRequest("BLISS10", -1m)));

        Assert.Equal(
            "Subtotal cannot be negative.",
            Assert.Single(failure.Errors["subtotal"]));
    }
}
