using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.UnitTests.ServiceCatalogue;

public class ServiceCategoryServiceTests
{
    private static SaveServiceCategoryRequest Request(
        string? name = "Hair",
        string? description = null,
        int sortOrder = 0,
        bool isActive = true) => new(name, description, sortOrder, isActive);

    [Fact]
    public async Task Create_saves_the_category_and_stamps_created_at()
    {
        using var harness = new CatalogueHarness();

        var created = await harness.Categories.CreateAsync(Request(description: "  Cut and colour  "));

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Hair", created.Name);
        Assert.Equal("Cut and colour", created.Description);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.ServiceCount);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);
        Assert.Null(created.UpdatedAt);

        await using var verify = harness.Reopen();

        Assert.Equal(1, verify.ServiceCategories.Count());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_missing_name(string? name)
    {
        using var harness = new CatalogueHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Categories.CreateAsync(Request(name)));

        Assert.Contains("name", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_name_longer_than_the_column()
    {
        using var harness = new CatalogueHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Categories.CreateAsync(
                Request(new string('x', ServiceCatalogueLimits.CategoryNameMaxLength + 1))));

        Assert.Contains("name", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_description_longer_than_the_column()
    {
        using var harness = new CatalogueHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Categories.CreateAsync(
                Request(description: new string('x', ServiceCatalogueLimits.CategoryDescriptionMaxLength + 1))));

        Assert.Contains("description", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_negative_display_position()
    {
        using var harness = new CatalogueHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Categories.CreateAsync(Request(sortOrder: -1)));

        Assert.Contains("sortOrder", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_reports_every_problem_at_once()
    {
        using var harness = new CatalogueHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Categories.CreateAsync(Request(name: "", sortOrder: -5)));

        Assert.Equal(
            new[] { "name", "sortOrder" },
            failure.Errors.Keys.OrderBy(key => key, StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("Hair", "hair")]
    [InlineData("Hair", " HAIR ")]
    public async Task Create_rejects_a_duplicate_name_regardless_of_case_and_padding(
        string existing,
        string attempted)
    {
        using var harness = new CatalogueHarness();

        await harness.Categories.CreateAsync(Request(existing));

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Categories.CreateAsync(Request(attempted)));

        Assert.Contains("already exists", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_renames_the_category_and_stamps_updated_at()
    {
        using var harness = new CatalogueHarness();

        var created = await harness.Categories.CreateAsync(Request());

        harness.Database.Clock.Advance(TimeSpan.FromMinutes(5));

        var updated = await harness.Categories.UpdateAsync(
            created.Id,
            Request(name: "Hair & Beauty", description: "Everything hair", sortOrder: 3, isActive: false));

        Assert.Equal("Hair & Beauty", updated.Name);
        Assert.Equal("Everything hair", updated.Description);
        Assert.Equal(3, updated.SortOrder);
        Assert.False(updated.IsActive);
        Assert.Equal(harness.Database.Clock.Now, updated.UpdatedAt);
        Assert.Equal(created.CreatedAt, updated.CreatedAt);
    }

    [Fact]
    public async Task Update_lets_a_category_keep_its_own_name()
    {
        using var harness = new CatalogueHarness();

        var created = await harness.Categories.CreateAsync(Request());

        var updated = await harness.Categories.UpdateAsync(created.Id, Request(description: "Unchanged name"));

        Assert.Equal("Hair", updated.Name);
    }

    [Fact]
    public async Task Update_rejects_a_name_taken_by_another_category()
    {
        using var harness = new CatalogueHarness();

        await harness.Categories.CreateAsync(Request(name: "Nails"));
        var hair = await harness.Categories.CreateAsync(Request(name: "Hair"));

        await Assert.ThrowsAsync<ConflictException>(
            () => harness.Categories.UpdateAsync(hair.Id, Request(name: "nails")));
    }

    [Fact]
    public async Task Update_throws_not_found_for_an_unknown_id()
    {
        using var harness = new CatalogueHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Categories.UpdateAsync(Guid.NewGuid(), Request()));
    }

    [Fact]
    public async Task GetCategory_throws_not_found_for_an_unknown_id()
    {
        using var harness = new CatalogueHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Categories.GetCategoryAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetStatus_deactivates_a_category_without_touching_the_services_under_it()
    {
        using var harness = new CatalogueHarness();

        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        var deactivated = await harness.Categories.SetStatusAsync(categoryId, isActive: false);

        Assert.False(deactivated.IsActive);

        await using var verify = harness.Reopen();

        // The category row survives so history stays resolvable, and the service
        // under it is untouched — it simply stops being bookable.
        Assert.Equal(1, verify.ServiceCategories.Count());
        Assert.Equal(1, verify.Services.Count());
        Assert.True(verify.Services.Single(service => service.Id == serviceId).IsActive);
    }

    [Fact]
    public async Task SetStatus_can_reactivate_a_category()
    {
        using var harness = new CatalogueHarness();

        var categoryId = await harness.SeedCategoryAsync("Hair", isActive: false);

        var reactivated = await harness.Categories.SetStatusAsync(categoryId, isActive: true);

        Assert.True(reactivated.IsActive);
    }

    [Fact]
    public async Task SetStatus_throws_not_found_for_an_unknown_id()
    {
        using var harness = new CatalogueHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Categories.SetStatusAsync(Guid.NewGuid(), isActive: false));
    }

    [Fact]
    public async Task GetCategories_reports_how_many_services_each_category_holds()
    {
        using var harness = new CatalogueHarness();

        var hair = await harness.SeedCategoryAsync("Hair", sortOrder: 0);
        var nails = await harness.SeedCategoryAsync("Nails", sortOrder: 1);
        await harness.SeedCategoryAsync("Threading", sortOrder: 2);

        await harness.SeedServiceAsync(hair, "Haircut", 60m);
        await harness.SeedServiceAsync(hair, "Blowdry", 60m);
        await harness.SeedServiceAsync(nails, "Manicure", 45m);

        var categories = await harness.Categories.GetCategoriesAsync(ServiceStatusFilter.All);

        Assert.Equal(new[] { "Hair", "Nails", "Threading" }, categories.Select(category => category.Name).ToArray());
        Assert.Equal(new[] { 2, 1, 0 }, categories.Select(category => category.ServiceCount).ToArray());
    }

    [Fact]
    public async Task GetCategories_can_be_narrowed_to_active_ones()
    {
        using var harness = await SeedOneOfEachStatus();

        var categories = await harness.Categories.GetCategoriesAsync(ServiceStatusFilter.Active);

        Assert.Equal(new[] { "Hair" }, categories.Select(category => category.Name).ToArray());
    }

    [Fact]
    public async Task GetCategories_can_be_narrowed_to_inactive_ones()
    {
        using var harness = await SeedOneOfEachStatus();

        var categories = await harness.Categories.GetCategoriesAsync(ServiceStatusFilter.Inactive);

        Assert.Equal(new[] { "Nails" }, categories.Select(category => category.Name).ToArray());
    }

    private static async Task<CatalogueHarness> SeedOneOfEachStatus()
    {
        var harness = new CatalogueHarness();

        await harness.SeedCategoryAsync("Hair", isActive: true, sortOrder: 0);
        await harness.SeedCategoryAsync("Nails", isActive: false, sortOrder: 1);

        return harness;
    }
}
