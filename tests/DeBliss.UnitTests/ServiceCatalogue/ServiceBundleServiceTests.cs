using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.ServiceCatalogue;

/// <summary>
/// A two-service bundle over a 120 + 40 catalogue: the standard total is 160,
/// so the default 140 price leaves a 20 saving, and the 45 + 30 durations give
/// a 75-minute window. Both components are staffed, so the bundle is bookable.
/// </summary>
public class ServiceBundleServiceTests
{
    private sealed record Fixture(
        CatalogueHarness Harness,
        Guid CategoryId,
        Guid StaffId,
        Guid HaircutId,
        Guid BeardTrimId);

    private static async Task<Fixture> ArrangeAsync()
    {
        var harness = new CatalogueHarness();
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var haircutId = await harness.SeedServiceAsync(
            categoryId, "Haircut", 120m, 45, staffIds: [staffId]);
        var beardTrimId = await harness.SeedServiceAsync(
            categoryId, "Beard Trim", 40m, 30, staffIds: [staffId]);

        return new Fixture(harness, categoryId, staffId, haircutId, beardTrimId);
    }

    private static SaveBundleRequest Request(
        string? name = "Bridal Package",
        Guid? categoryId = null,
        string? description = null,
        decimal price = 140m,
        bool isActive = true,
        int sortOrder = 0,
        IReadOnlyList<Guid>? items = null) =>
        new(name, categoryId, description, price, isActive, sortOrder, items);

    private static async Task<BundleDto> CreateAsync(Fixture fixture, SaveBundleRequest? request = null) =>
        await fixture.Harness.Bundles.CreateAsync(request ?? Request(
            categoryId: fixture.CategoryId,
            items: [fixture.HaircutId, fixture.BeardTrimId]));

    [Fact]
    public async Task Create_saves_the_bundle_with_its_components_in_the_submitted_order()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture, Request(
            categoryId: fixture.CategoryId,
            description: "  Cut, trim and a blow dry  ",
            items: [fixture.BeardTrimId, fixture.HaircutId]));

        Assert.Equal("Bridal Package", created.Name);
        Assert.Equal("Cut, trim and a blow dry", created.Description);
        Assert.Equal(fixture.CategoryId, created.CategoryId);
        Assert.Equal("Hair", created.CategoryName);
        Assert.True(created.IsCategoryActive);
        Assert.Equal(140m, created.Price);
        Assert.True(created.IsActive);
        Assert.True(created.IsBookable);
        Assert.Equal(fixture.Harness.Database.Clock.Now, created.CreatedAt);

        // Duration and the standard total are derived from the components, so
        // the menu, the booking window and the checkout math cannot disagree.
        Assert.Equal(75, created.DurationMinutes);
        Assert.Equal(160m, created.StandardTotalPrice);
        Assert.Equal(20m, created.SavingsAmount);

        Assert.Collection(
            created.Items,
            first =>
            {
                Assert.Equal(fixture.BeardTrimId, first.ServiceId);
                Assert.Equal("Beard Trim", first.Name);
                Assert.Equal(0, first.DisplayOrder);
                Assert.Equal(30, first.DurationMinutes);
                Assert.Equal(40m, first.Price);
                Assert.True(first.IsActive);
            },
            second =>
            {
                Assert.Equal(fixture.HaircutId, second.ServiceId);
                Assert.Equal(1, second.DisplayOrder);
            });

        await using var verify = fixture.Harness.Reopen();

        var stored = await verify.ServiceBundles
            .Include(bundle => bundle.Items)
            .SingleAsync();

        Assert.Equal("Bridal Package", stored.Name);
        Assert.Equal(140m, stored.Price);
        Assert.Equal(2, stored.Items.Count);
        Assert.All(stored.Items, item => Assert.Equal(1, item.Quantity));
    }

    [Fact]
    public async Task Create_can_price_a_bundle_at_exactly_the_component_total()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture, Request(
            categoryId: fixture.CategoryId,
            price: 160m,
            items: [fixture.HaircutId, fixture.BeardTrimId]));

        Assert.Equal(160m, created.Price);
        Assert.Equal(0m, created.SavingsAmount);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_missing_name(string? name)
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                name,
                fixture.CategoryId,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "Bundle name is required.",
            Assert.Single(failure.Errors["name"]));
    }

    [Fact]
    public async Task Create_rejects_a_name_longer_than_the_column()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                new string('x', ServiceCatalogueLimits.BundleNameMaxLength + 1),
                fixture.CategoryId,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            $"Bundle name must be {ServiceCatalogueLimits.BundleNameMaxLength} characters or fewer.",
            Assert.Single(failure.Errors["name"]));
    }

    [Fact]
    public async Task Create_rejects_a_description_longer_than_the_column()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                description: new string('x', ServiceCatalogueLimits.BundleDescriptionMaxLength + 1),
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            $"Description must be {ServiceCatalogueLimits.BundleDescriptionMaxLength} characters or fewer.",
            Assert.Single(failure.Errors["description"]));
    }

    [Fact]
    public async Task Create_requires_a_category()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "Choose a category for this bundle.",
            Assert.Single(failure.Errors["categoryId"]));
    }

    [Fact]
    public async Task Create_rejects_a_category_that_no_longer_exists()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: Guid.NewGuid(),
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "The selected category no longer exists. Choose another one.",
            Assert.Single(failure.Errors["categoryId"]));
    }

    [Fact]
    public async Task Create_rejects_a_negative_display_position()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                sortOrder: -1,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "Display position cannot be negative.",
            Assert.Single(failure.Errors["sortOrder"]));
    }

    [Fact]
    public async Task Create_requires_a_component_list()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(categoryId: fixture.CategoryId)));

        Assert.Equal(
            $"A bundle needs at least {DeBliss.Domain.Services.ServiceBundle.MinimumComponents} different services.",
            Assert.Single(failure.Errors["items"]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Create_rejects_fewer_than_two_components(int componentCount)
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var pool = new[] { fixture.HaircutId, fixture.BeardTrimId };

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                items: pool.Take(componentCount).ToList())));

        Assert.Equal(
            $"A bundle needs at least {DeBliss.Domain.Services.ServiceBundle.MinimumComponents} different services.",
            Assert.Single(failure.Errors["items"]));
    }

    [Fact]
    public async Task Create_rejects_the_same_component_twice()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                items: [fixture.HaircutId, fixture.HaircutId])));

        // A repeat also leaves only one distinct service, so the minimum-count
        // rule fires alongside it; both belong on the same field.
        Assert.Contains(
            "The same service cannot be included more than once in a bundle.",
            failure.Errors["items"]);
    }

    [Fact]
    public async Task Create_rejects_components_that_no_longer_exist()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                items: [fixture.HaircutId, Guid.NewGuid(), Guid.NewGuid()])));

        Assert.Equal(
            "2 of the selected services no longer exist. Remove them and try again.",
            Assert.Single(failure.Errors["items"]));
    }

    [Fact]
    public async Task Create_rejects_an_inactive_component_and_names_it()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var retiredId = await fixture.Harness.SeedServiceAsync(
            fixture.CategoryId, "Old Perm", 90m, isActive: false, staffIds: [fixture.StaffId]);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                items: [fixture.HaircutId, retiredId])));

        Assert.Equal(
            "Only active services can go in a bundle. Deactivate these instead of removing them: Old Perm.",
            Assert.Single(failure.Errors["items"]));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public async Task Create_rejects_a_negative_price(decimal price)
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                price: price,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "Bundle price cannot be negative.",
            Assert.Single(failure.Errors["price"]));
    }

    [Fact]
    public async Task Create_rejects_a_price_above_the_column_maximum()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                price: ServiceCatalogueLimits.MaximumPrice + 0.01m,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            $"Bundle price cannot be more than {ServiceCatalogueLimits.MaximumPrice:N2}.",
            Assert.Single(failure.Errors["price"]));
    }

    [Fact]
    public async Task Create_rejects_a_price_with_more_than_two_decimal_places()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                price: 140.005m,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Equal(
            "Bundle price can have at most two decimal places.",
            Assert.Single(failure.Errors["price"]));
    }

    [Fact]
    public async Task Create_rejects_a_price_above_the_component_total()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                price: 160.01m,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Contains(
            "Bundle price cannot exceed the combined individual price of the included services",
            Assert.Single(failure.Errors["price"]));
    }

    [Fact]
    public async Task Create_reports_every_field_problem_at_once()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                name: "   ",
                categoryId: fixture.CategoryId,
                price: -5m,
                sortOrder: -1,
                items: [fixture.HaircutId])));

        Assert.Equal(4, failure.Errors.Count);
        Assert.Contains("name", failure.Errors.Keys);
        Assert.Contains("price", failure.Errors.Keys);
        Assert.Contains("sortOrder", failure.Errors.Keys);
        Assert.Contains("items", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_in_the_same_category()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await CreateAsync(fixture);

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => CreateAsync(fixture));

        Assert.Equal(
            "A bundle named 'Bridal Package' already exists in the 'Hair' category.",
            failure.Message);
    }

    [Fact]
    public async Task The_same_bundle_name_is_allowed_in_another_category()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await CreateAsync(fixture);
        var otherCategoryId = await fixture.Harness.SeedCategoryAsync("Bridal");

        var created = await CreateAsync(fixture, Request(
            categoryId: otherCategoryId,
            items: [fixture.HaircutId, fixture.BeardTrimId]));

        Assert.Equal(otherCategoryId, created.CategoryId);
        Assert.Equal("Bridal", created.CategoryName);
    }

    [Fact]
    public async Task Field_problems_are_reported_before_the_duplicate_name_conflict()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await CreateAsync(fixture);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => CreateAsync(fixture, Request(
                categoryId: fixture.CategoryId,
                price: 999m,
                items: [fixture.HaircutId, fixture.BeardTrimId])));

        Assert.Contains("price", failure.Errors.Keys);
    }

    [Fact]
    public async Task Get_returns_the_bundle_with_its_components_in_sequence()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture, Request(
            categoryId: fixture.CategoryId,
            items: [fixture.BeardTrimId, fixture.HaircutId]));

        var loaded = await fixture.Harness.Bundles.GetAsync(created.Id);

        Assert.Equal(created.Id, loaded.Id);
        Assert.Equal(75, loaded.DurationMinutes);
        Assert.Equal(160m, loaded.StandardTotalPrice);
        Assert.Equal(
            new[] { "Beard Trim", "Haircut" },
            loaded.Items.Select(item => item.Name).ToArray());
        Assert.Equal(
            new[] { 0, 1 },
            loaded.Items.Select(item => item.DisplayOrder).ToArray());
    }

    [Fact]
    public async Task Getting_an_unknown_bundle_is_not_found()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var missingId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Harness.Bundles.GetAsync(missingId));

        Assert.Contains(missingId.ToString(), failure.Message);
    }

    [Fact]
    public async Task Update_reorders_components_without_rewriting_their_rows()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture);

        Guid haircutRowId;
        Guid beardTrimRowId;

        await using (var before = fixture.Harness.Reopen())
        {
            var rows = await before.ServiceBundleItems.ToDictionaryAsync(item => item.ServiceId);
            haircutRowId = rows[fixture.HaircutId].Id;
            beardTrimRowId = rows[fixture.BeardTrimId].Id;
        }

        var updated = await fixture.Harness.Bundles.UpdateAsync(created.Id, Request(
            name: "Bridal Package",
            categoryId: fixture.CategoryId,
            price: 150m,
            items: [fixture.BeardTrimId, fixture.HaircutId]));

        Assert.Equal(150m, updated.Price);
        Assert.Equal(10m, updated.SavingsAmount);
        Assert.Equal(
            new[] { "Beard Trim", "Haircut" },
            updated.Items.Select(item => item.Name).ToArray());

        await using var verify = fixture.Harness.Reopen();

        var after = await verify.ServiceBundleItems.ToListAsync();

        // The same two rows, only their positions moved: a reorder is not a
        // delete-and-reinsert, so nothing that points at a component loses it.
        Assert.Equal(2, after.Count);

        var haircutRow = after.Single(item => item.ServiceId == fixture.HaircutId);
        var beardTrimRow = after.Single(item => item.ServiceId == fixture.BeardTrimId);

        Assert.Equal(haircutRowId, haircutRow.Id);
        Assert.Equal(1, haircutRow.DisplayOrder);
        Assert.Equal(beardTrimRowId, beardTrimRow.Id);
        Assert.Equal(0, beardTrimRow.DisplayOrder);
    }

    [Fact]
    public async Task Update_removes_the_components_that_are_no_longer_submitted()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var thirdId = await fixture.Harness.SeedServiceAsync(
            fixture.CategoryId, "Blow Dry", 50m, 20, staffIds: [fixture.StaffId]);

        var created = await CreateAsync(fixture, Request(
            categoryId: fixture.CategoryId,
            price: 200m,
            items: [fixture.HaircutId, fixture.BeardTrimId, thirdId]));

        Assert.Equal(3, created.Items.Count);

        var updated = await fixture.Harness.Bundles.UpdateAsync(created.Id, Request(
            name: "Bridal Package",
            categoryId: fixture.CategoryId,
            price: 150m,
            items: [fixture.HaircutId, thirdId]));

        Assert.Equal(2, updated.Items.Count);
        Assert.Equal(65, updated.DurationMinutes);
        Assert.Equal(170m, updated.StandardTotalPrice);

        await using var verify = fixture.Harness.Reopen();

        Assert.Equal(2, await verify.ServiceBundleItems.CountAsync());
        Assert.Equal(0, await verify.ServiceBundleItems.CountAsync(item => item.ServiceId == fixture.BeardTrimId));
    }

    [Fact]
    public async Task Update_keeps_the_bundle_on_its_own_name()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture);

        var updated = await fixture.Harness.Bundles.UpdateAsync(created.Id, Request(
            name: "BRIDAL PACKAGE",
            categoryId: fixture.CategoryId,
            price: 145m,
            items: [fixture.HaircutId, fixture.BeardTrimId]));

        Assert.Equal("BRIDAL PACKAGE", updated.Name);
        Assert.Equal(145m, updated.Price);
    }

    [Fact]
    public async Task Update_validates_before_loading_so_an_unknown_bundle_is_not_found()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => fixture.Harness.Bundles.UpdateAsync(Guid.NewGuid(), Request(
                name: "  ",
                categoryId: fixture.CategoryId,
                items: [fixture.HaircutId])));

        Assert.Contains("name", failure.Errors.Keys);
    }

    [Fact]
    public async Task Deactivating_a_bundle_hides_it_but_keeps_the_components()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture);

        // The status call is its own web request, so it starts from an empty
        // tracker and reads the whole component graph from the database.
        fixture.Harness.Context.ChangeTracker.Clear();

        var deactivated = await fixture.Harness.Bundles.SetStatusAsync(created.Id, false);

        Assert.False(deactivated.IsActive);
        Assert.False(deactivated.IsBookable);
        Assert.Equal(2, deactivated.Items.Count);

        await using var verify = fixture.Harness.Reopen();

        Assert.False((await verify.ServiceBundles.SingleAsync()).IsActive);
        Assert.Equal(2, await verify.ServiceBundleItems.CountAsync());
    }

    [Fact]
    public async Task Reactivating_a_bundle_restores_its_bookability()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await CreateAsync(fixture);
        fixture.Harness.Context.ChangeTracker.Clear();

        await fixture.Harness.Bundles.SetStatusAsync(created.Id, false);
        fixture.Harness.Context.ChangeTracker.Clear();

        var reactivated = await fixture.Harness.Bundles.SetStatusAsync(created.Id, true);

        Assert.True(reactivated.IsActive);
        Assert.True(reactivated.IsBookable);
    }

    [Fact]
    public async Task Activating_is_refused_while_a_component_cannot_be_booked()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        // No staff member offers this service, so no bundle containing it can
        // ever be booked — activating one would put an unbookable entry on the menu.
        var unstaffedId = await fixture.Harness.SeedServiceAsync(
            fixture.CategoryId, "Aromatherapy", 70m, staffIds: null);

        var bundleId = await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Spa Morning", 90m, [fixture.HaircutId, unstaffedId], isActive: false);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => fixture.Harness.Bundles.SetStatusAsync(bundleId, true));

        Assert.Equal(
            "Every service in the bundle must itself be active and bookable before the bundle can be activated.",
            Assert.Single(failure.Errors["isActive"]));

        await using var verify = fixture.Harness.Reopen();

        Assert.False((await verify.ServiceBundles.SingleAsync()).IsActive);
    }

    [Fact]
    public async Task Activating_is_refused_while_a_component_has_been_deactivated()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var bundleId = await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Spa Morning", 150m, [fixture.HaircutId, fixture.BeardTrimId]);

        await using (var context = fixture.Harness.Reopen())
        {
            var beardTrim = await context.Services.SingleAsync(service => service.Id == fixture.BeardTrimId);
            beardTrim.IsActive = false;
            await context.SaveChangesAsync();
        }

        fixture.Harness.Context.ChangeTracker.Clear();

        await fixture.Harness.Bundles.SetStatusAsync(bundleId, false);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => fixture.Harness.Bundles.SetStatusAsync(bundleId, true));

        Assert.Contains("isActive", failure.Errors.Keys);
    }

    [Fact]
    public async Task Setting_the_status_of_an_unknown_bundle_is_not_found()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var missingId = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => fixture.Harness.Bundles.SetStatusAsync(missingId, false));

        Assert.Contains(missingId.ToString(), failure.Message);
    }

    [Fact]
    public async Task The_bookable_list_skips_bundles_that_cannot_be_booked()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var bookableId = await CreateAsync(fixture);

        await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Switched Off", 150m, [fixture.HaircutId, fixture.BeardTrimId], isActive: false);

        var unstaffedId = await fixture.Harness.SeedServiceAsync(
            fixture.CategoryId, "Aromatherapy", 70m);

        await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Unstaffed", 100m, [fixture.HaircutId, unstaffedId]);

        var inactiveCategoryId = await fixture.Harness.SeedCategoryAsync("Retired", isActive: false);

        await fixture.Harness.SeedBundleAsync(
            inactiveCategoryId, "Hidden Category", 150m, [fixture.HaircutId, fixture.BeardTrimId]);

        var bookable = await fixture.Harness.Bundles.GetBookableAsync();

        var only = Assert.Single(bookable);

        Assert.Equal(bookableId.Id, only.Id);
        Assert.Equal(2, only.Items.Count);
        Assert.Equal(160m, only.StandardTotalPrice);
    }

    [Fact]
    public async Task A_bundle_stays_on_the_menu_while_it_cannot_be_booked()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var bundleId = await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Spa Morning", 150m, [fixture.HaircutId, fixture.BeardTrimId]);

        await using (var context = fixture.Harness.Reopen())
        {
            var beardTrim = await context.Services.SingleAsync(service => service.Id == fixture.BeardTrimId);
            beardTrim.IsActive = false;
            await context.SaveChangesAsync();
        }

        fixture.Harness.Context.ChangeTracker.Clear();

        var loaded = await fixture.Harness.Bundles.GetAsync(bundleId);

        Assert.True(loaded.IsActive);
        Assert.False(loaded.IsBookable);
        Assert.False(loaded.Items.Single(item => item.ServiceId == fixture.BeardTrimId).IsActive);
    }
}
