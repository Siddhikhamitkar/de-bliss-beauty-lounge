using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.ServiceCatalogue;

public class ServiceCatalogueServiceTests
{
    private static SaveServiceRequest Request(
        string? name = "Haircut",
        Guid? categoryId = null,
        string? description = null,
        int durationMinutes = 45,
        decimal price = 60.00m,
        bool isActive = true,
        bool isOnlineBookable = false,
        int sortOrder = 0,
        IReadOnlyList<Guid>? staffIds = null) =>
        new(name, categoryId, description, durationMinutes, price, isActive, isOnlineBookable, sortOrder, staffIds);

    private static ServiceQuery Query(
        string? search = null,
        Guid? categoryId = null,
        ServiceStatusFilter status = ServiceStatusFilter.All,
        bool bookableOnly = false,
        int page = 1,
        int pageSize = 50) => new(search, categoryId, status, bookableOnly, page, pageSize);

    private sealed record Fixture(CatalogueHarness Harness, Guid CategoryId, Guid StaffId);

    private static async Task<Fixture> ArrangeAsync()
    {
        var harness = new CatalogueHarness();
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");

        return new Fixture(harness, categoryId, staffId);
    }

    [Fact]
    public async Task Create_saves_the_service_with_its_price_duration_and_category()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var created = await harness.Catalogue.CreateAsync(Request(
            categoryId: categoryId,
            description: "  Wash, cut and blow dry  ",
            durationMinutes: 45,
            price: 60.50m,
            staffIds: [staffId]));

        Assert.Equal("Haircut", created.Name);
        Assert.Equal("Wash, cut and blow dry", created.Description);
        Assert.Equal(categoryId, created.CategoryId);
        Assert.Equal("Hair", created.CategoryName);
        Assert.True(created.IsCategoryActive);
        Assert.Equal(45, created.DurationMinutes);
        Assert.Equal(60.50m, created.Price);
        Assert.True(created.IsActive);
        Assert.True(created.IsBookable);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);

        await using var verify = harness.Reopen();

        var stored = await verify.Services.SingleAsync();

        Assert.Equal(60.50m, stored.Price);
        Assert.IsType<decimal>(stored.Price);
    }

    [Fact]
    public async Task Create_assigns_the_eligible_staff_and_ignores_repeats()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var second = await harness.SeedStaffAsync("Erica", "Mae");

        var created = await harness.Catalogue.CreateAsync(Request(
            categoryId: categoryId,
            staffIds: [staffId, second, staffId, Guid.Empty]));

        Assert.Equal(2, created.EligibleStaff.Count);
        Assert.Equal(
            new[] { "Erica", "Jasmin" },
            created.EligibleStaff.Select(member => member.FirstName).ToArray());

        await using var verify = harness.Reopen();

        Assert.Equal(2, await verify.StaffServices.CountAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_rejects_a_missing_name(string? name)
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(name, categoryId, staffIds: [staffId])));

        Assert.Contains("name", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_name_longer_than_the_column()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                new string('x', ServiceCatalogueLimits.ServiceNameMaxLength + 1),
                categoryId,
                staffIds: [staffId])));

        Assert.Contains("name", failure.Errors.Keys);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-15)]
    public async Task Create_rejects_a_duration_that_is_not_positive(int durationMinutes)
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                durationMinutes: durationMinutes,
                staffIds: [staffId])));

        Assert.Contains("durationMinutes", failure.Errors.Keys);
        Assert.Contains("greater than zero", failure.Errors["durationMinutes"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_a_duration_longer_than_a_day()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                durationMinutes: ServiceCatalogueLimits.MaximumDurationMinutes + 1,
                staffIds: [staffId])));

        Assert.Contains("durationMinutes", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_negative_price()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                price: -0.01m,
                staffIds: [staffId])));

        Assert.Contains("price", failure.Errors.Keys);
        Assert.Contains("negative", failure.Errors["price"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_accepts_a_free_service()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var created = await harness.Catalogue.CreateAsync(Request(
            categoryId: categoryId,
            price: 0m,
            staffIds: [staffId]));

        Assert.Equal(0m, created.Price);
    }

    [Fact]
    public async Task Create_rejects_a_price_with_more_than_two_decimal_places()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                price: 60.999m,
                staffIds: [staffId])));

        Assert.Contains("price", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_price_the_column_cannot_hold()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                price: ServiceCatalogueLimits.MaximumPrice + 1m,
                staffIds: [staffId])));

        Assert.Contains("price", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_requires_a_category()
    {
        var (harness, _, staffId) = await ArrangeAsync();
        using var _2 = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(categoryId: null, staffIds: [staffId])));

        Assert.Contains("categoryId", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_category_that_does_not_exist()
    {
        var (harness, _, staffId) = await ArrangeAsync();
        using var _2 = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(categoryId: Guid.NewGuid(), staffIds: [staffId])));

        Assert.Contains("categoryId", failure.Errors.Keys);
        Assert.Contains("no longer exists", failure.Errors["categoryId"][0], StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_an_active_service_that_nobody_can_perform()
    {
        var (harness, categoryId, _) = await ArrangeAsync();
        using var _2 = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(categoryId: categoryId, staffIds: null)));

        Assert.Contains("eligibleStaffIds", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_an_active_service_whose_only_staff_member_is_inactive()
    {
        var (harness, categoryId, _) = await ArrangeAsync();
        using var _2 = harness;

        var onLeave = await harness.SeedStaffAsync("Sonal", "Kapoor", isActive: false);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(categoryId: categoryId, staffIds: [onLeave])));

        Assert.Contains("eligibleStaffIds", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_allows_a_draft_service_with_no_staff_yet()
    {
        var (harness, categoryId, _) = await ArrangeAsync();
        using var _2 = harness;

        var created = await harness.Catalogue.CreateAsync(Request(
            categoryId: categoryId,
            isActive: false,
            staffIds: null));

        Assert.False(created.IsActive);
        Assert.False(created.IsBookable);
        Assert.Empty(created.EligibleStaff);
    }

    [Fact]
    public async Task Create_rejects_staff_ids_that_do_not_exist()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                categoryId: categoryId,
                staffIds: [staffId, Guid.NewGuid()])));

        Assert.Contains("eligibleStaffIds", failure.Errors.Keys);
    }

    [Theory]
    [InlineData("Haircut", "haircut")]
    [InlineData("Haircut", "  HAIRCUT ")]
    public async Task Create_rejects_a_duplicate_name_in_the_same_category(
        string existing,
        string attempted)
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.Catalogue.CreateAsync(Request(existing, categoryId, staffIds: [staffId]));

        var conflict = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Catalogue.CreateAsync(Request(attempted, categoryId, staffIds: [staffId])));

        Assert.Contains("Hair", conflict.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_allows_the_same_name_in_a_different_category()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var nails = await harness.SeedCategoryAsync("Nails");

        await harness.Catalogue.CreateAsync(Request("Express finish", categoryId, staffIds: [staffId]));

        var alsoNamedTheSame = await harness.Catalogue.CreateAsync(
            Request("Express finish", nails, staffIds: [staffId]));

        Assert.Equal(nails, alsoNamedTheSame.CategoryId);
    }

    [Fact]
    public async Task Create_reports_validation_errors_rather_than_a_name_conflict()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.Catalogue.CreateAsync(Request("Haircut", categoryId, staffIds: [staffId]));

        // The name collides but the duration is also invalid: 400 wins over 409
        // because the request could never have been saved as it stands.
        await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.CreateAsync(Request(
                "Haircut",
                categoryId,
                durationMinutes: 0,
                staffIds: [staffId])));
    }

    [Fact]
    public async Task Update_changes_the_current_price_without_rewriting_booked_history()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Keratin Treatment", 350m, 120, staffIds: [staffId]);
        var appointmentId = await harness.SeedBookedServiceAsync(
            serviceId,
            snapshotName: "Keratin Treatment",
            unitPrice: 299m,
            durationMinutes: 90);

        harness.Database.Clock.Advance(TimeSpan.FromDays(3));

        var updated = await harness.Catalogue.UpdateAsync(serviceId, Request(
            "Keratin Treatment",
            categoryId,
            durationMinutes: 120,
            price: 399m,
            staffIds: [staffId]));

        Assert.Equal(399m, updated.Price);
        Assert.NotNull(updated.UpdatedAt);

        await using var verify = harness.Reopen();

        var bookedLine = await verify.AppointmentServices
            .Where(line => line.AppointmentId == appointmentId)
            .SingleAsync();

        // The appointment keeps what was actually charged and booked.
        Assert.Equal(299m, bookedLine.UnitPrice);
        Assert.Equal(90, bookedLine.DurationMinutes);
        Assert.Equal("Keratin Treatment", bookedLine.ServiceNameSnapshot);
        Assert.Null(bookedLine.UpdatedAt);
    }

    [Fact]
    public async Task Update_can_move_a_service_to_another_category_and_replace_its_staff()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var nails = await harness.SeedCategoryAsync("Nails");
        var erica = await harness.SeedStaffAsync("Erica", "Mae");
        var joy = await harness.SeedStaffAsync("Joy", "D'Souza");

        var serviceId = await harness.SeedServiceAsync(categoryId, "Gel finish", 90m, staffIds: [staffId, erica]);

        var updated = await harness.Catalogue.UpdateAsync(serviceId, Request(
            "Gel finish",
            nails,
            durationMinutes: 60,
            price: 120m,
            staffIds: [joy]));

        Assert.Equal(nails, updated.CategoryId);
        Assert.Equal("Nails", updated.CategoryName);
        Assert.Equal(60, updated.DurationMinutes);
        Assert.Equal(120m, updated.Price);
        Assert.Equal(new[] { joy }, updated.EligibleStaff.Select(member => member.StaffId).ToArray());

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.StaffServices.CountAsync(line => line.ServiceId == serviceId));
    }

    [Fact]
    public async Task Update_lets_a_service_keep_its_own_name()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        var updated = await harness.Catalogue.UpdateAsync(
            serviceId,
            Request("haircut", categoryId, price: 75m, staffIds: [staffId]));

        Assert.Equal(75m, updated.Price);
    }

    [Fact]
    public async Task Update_rejects_a_name_taken_by_a_sibling_service()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedServiceAsync(categoryId, "Blowdry", 60m, staffIds: [staffId]);
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        await Assert.ThrowsAsync<ConflictException>(
            () => harness.Catalogue.UpdateAsync(serviceId, Request("blowdry", categoryId, staffIds: [staffId])));
    }

    [Fact]
    public async Task Update_throws_not_found_for_an_unknown_id()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Catalogue.UpdateAsync(
                Guid.NewGuid(),
                Request(categoryId: categoryId, staffIds: [staffId])));
    }

    [Fact]
    public async Task GetAsync_throws_not_found_for_an_unknown_id()
    {
        var (harness, _, _) = await ArrangeAsync();
        using var _2 = harness;

        await Assert.ThrowsAsync<NotFoundException>(() => harness.Catalogue.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task SetStatus_deactivates_a_service_and_keeps_everything_that_references_it()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedBookedServiceAsync(serviceId, "Haircut", 60m, 45);

        var deactivated = await harness.Catalogue.SetStatusAsync(serviceId, isActive: false);

        Assert.False(deactivated.IsActive);
        Assert.False(deactivated.IsBookable);

        await using var verify = harness.Reopen();

        // Nothing was deleted: the service row and the booked line both survive.
        Assert.Equal(1, await verify.Services.CountAsync());
        Assert.Equal(1, await verify.StaffServices.CountAsync());
        Assert.Equal(1, await verify.AppointmentServices.CountAsync());
    }

    [Fact]
    public async Task SetStatus_refuses_to_activate_a_service_nobody_can_perform()
    {
        var (harness, categoryId, _) = await ArrangeAsync();
        using var _2 = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, isActive: false);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Catalogue.SetStatusAsync(serviceId, isActive: true));

        Assert.Contains("eligibleStaffIds", failure.Errors.Keys);
    }

    [Fact]
    public async Task SetStatus_activates_a_service_once_an_active_staff_member_is_assigned()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(
            categoryId,
            "Haircut",
            60m,
            isActive: false,
            staffIds: [staffId]);

        var activated = await harness.Catalogue.SetStatusAsync(serviceId, isActive: true);

        Assert.True(activated.IsActive);
        Assert.True(activated.IsBookable);
    }

    [Fact]
    public async Task SetStatus_throws_not_found_for_an_unknown_id()
    {
        var (harness, _, _) = await ArrangeAsync();
        using var _2 = harness;

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Catalogue.SetStatusAsync(Guid.NewGuid(), isActive: false));
    }

    [Fact]
    public async Task Search_can_be_narrowed_to_one_category()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var nails = await harness.SeedCategoryAsync("Nails", sortOrder: 1);

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(nails, "Manicure", 45m, staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query(categoryId: nails));

        Assert.Equal(1, page.TotalCount);
        Assert.Equal("Manicure", Assert.Single(page.Items).Name);
    }

    [Theory]
    [InlineData("ker", "Keratin Treatment")]
    [InlineData("KERATIN", "Keratin Treatment")]
    [InlineData("smoothing", "Keratin Treatment")]
    public async Task Search_matches_the_name_or_the_description_ignoring_case(
        string term,
        string expected)
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(
            categoryId,
            "Keratin Treatment",
            350m,
            description: "Deep smoothing for frizzy hair",
            staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query(search: term));

        Assert.Equal(expected, Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Search_returns_an_empty_page_when_nothing_matches()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query(search: "microblading"));

        Assert.Equal(0, page.TotalCount);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task Search_can_be_narrowed_by_status()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(categoryId, "Retired perm", 80m, isActive: false, staffIds: [staffId]);

        var active = await harness.Catalogue.SearchAsync(Query(status: ServiceStatusFilter.Active));
        var inactive = await harness.Catalogue.SearchAsync(Query(status: ServiceStatusFilter.Inactive));

        Assert.Equal(new[] { "Haircut" }, active.Items.Select(service => service.Name).ToArray());
        Assert.Equal(new[] { "Retired perm" }, inactive.Items.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task Search_paginates_and_reports_the_total_across_pages()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        for (var index = 0; index < 5; index++)
        {
            await harness.SeedServiceAsync(
                categoryId,
                $"Service {index:D2}",
                10m + index,
                staffIds: [staffId]);
        }

        var secondPage = await harness.Catalogue.SearchAsync(Query(page: 2, pageSize: 2));

        Assert.Equal(5, secondPage.TotalCount);
        Assert.Equal(2, secondPage.PageSize);
        Assert.Equal(3, secondPage.TotalPages);
        Assert.True(secondPage.HasPreviousPage);
        Assert.True(secondPage.HasNextPage);
        Assert.Equal(
            new[] { "Service 02", "Service 03" },
            secondPage.Items.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task Search_orders_by_category_position_then_service_position_then_name()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var nails = await harness.SeedCategoryAsync("Nails", sortOrder: 1);

        // Hair was seeded at position 0, so push it behind Nails: the ordering
        // under test must come from the category position, not from luck.
        await harness.Categories.UpdateAsync(
            categoryId,
            new SaveServiceCategoryRequest("Hair", null, 2, IsActive: true));

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(categoryId, "Blowdry", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(nails, "Manicure", 45m, staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query());

        Assert.Equal(
            new[] { "Manicure", "Blowdry", "Haircut" },
            page.Items.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task Search_reports_how_many_staff_can_perform_each_service()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var erica = await harness.SeedStaffAsync("Erica", "Mae");

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId, erica]);
        await harness.SeedServiceAsync(categoryId, "Retired perm", 80m, isActive: false, staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query());

        var counts = page.Items.ToDictionary(service => service.Name, service => service.EligibleStaffCount);

        Assert.Equal(2, counts["Haircut"]);
        Assert.Equal(1, counts["Retired perm"]);
    }

    [Fact]
    public async Task Search_flags_a_service_as_unbookable_while_its_category_is_inactive()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        await harness.Categories.SetStatusAsync(categoryId, isActive: false);

        var page = await harness.Catalogue.SearchAsync(Query());

        var row = Assert.Single(page.Items);

        Assert.Equal(serviceId, row.Id);
        Assert.True(row.IsActive);
        Assert.False(row.IsBookable);
    }

    [Fact]
    public async Task Search_can_be_restricted_to_what_may_be_booked()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(categoryId, "Retired perm", 80m, isActive: false, staffIds: [staffId]);

        var page = await harness.Catalogue.SearchAsync(Query(bookableOnly: true));

        Assert.Equal(new[] { "Haircut" }, page.Items.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task Search_lists_bundles_interleaved_with_services_within_their_category()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var nails = await harness.SeedCategoryAsync("Nails", sortOrder: 1);

        var haircutId = await harness.SeedServiceAsync(
            categoryId, "Haircut", 60m, staffIds: [staffId], sortOrder: 1);
        var blowDryId = await harness.SeedServiceAsync(
            categoryId, "Blow Dry", 20m, staffIds: [staffId], sortOrder: 2);
        await harness.SeedServiceAsync(nails, "Manicure", 45m, staffIds: [staffId]);

        await harness.SeedBundleAsync(categoryId, "Bridal Day", 70m, [haircutId, blowDryId]);

        // A bundle may share a name with a service, so the two "Haircut" rows
        // are separated by the kind, which is the last thing the menu sorts on.
        await harness.SeedBundleAsync(categoryId, "Haircut", 75m, [haircutId, blowDryId], sortOrder: 1);

        var page = await harness.Catalogue.SearchAsync(Query());

        Assert.Equal(
            new[]
            {
                ("Bridal Day", "Bundle"),
                ("Haircut", "Bundle"),
                ("Haircut", "Service"),
                ("Blow Dry", "Service"),
                ("Manicure", "Service")
            },
            page.Items.Select(item => (item.Name, item.ItemKind)).ToArray());
    }

    [Fact]
    public async Task Search_reports_a_bundle_row_with_its_components_and_the_standard_total()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var haircutId = await harness.SeedServiceAsync(
            categoryId, "Haircut", 120m, 45, staffIds: [staffId]);
        var beardTrimId = await harness.SeedServiceAsync(
            categoryId, "Beard Trim", 40m, 30, staffIds: [staffId]);

        var bundleId = await harness.SeedBundleAsync(
            categoryId, "Grooming Day", 140m, [haircutId, beardTrimId]);

        var page = await harness.Catalogue.SearchAsync(Query());

        var row = Assert.Single(page.Items, item => item.ItemKind == "Bundle");

        Assert.Equal(bundleId, row.Id);
        Assert.Equal(2, row.ServiceCount);
        Assert.Equal(160m, row.StandardTotalPrice);
        Assert.Equal(140m, row.Price);
        Assert.Equal(75, row.DurationMinutes);
        Assert.Equal("Hair", row.CategoryName);
        Assert.True(row.IsActive);
        Assert.True(row.IsBookable);
        Assert.False(row.IsOnlineBookable);
        Assert.Equal(0, row.EligibleStaffCount);
    }

    [Fact]
    public async Task Search_leaves_the_bundle_columns_empty_on_a_service_row()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);

        var row = Assert.Single((await harness.Catalogue.SearchAsync(Query())).Items);

        Assert.Equal(serviceId, row.Id);
        Assert.Equal("Service", row.ItemKind);
        Assert.Null(row.ServiceCount);
        Assert.Null(row.StandardTotalPrice);
    }

    [Fact]
    public async Task Search_matches_a_bundle_by_its_name_or_its_description()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var haircutId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        var blowDryId = await harness.SeedServiceAsync(categoryId, "Blow Dry", 20m, staffIds: [staffId]);

        await harness.SeedBundleAsync(
            categoryId,
            "Bridal Day",
            70m,
            [haircutId, blowDryId],
            description: "Everything the bride needs before the ceremony");
        await harness.SeedBundleAsync(categoryId, "Grooming Day", 70m, [haircutId, blowDryId]);

        var byName = await harness.Catalogue.SearchAsync(Query(search: "BRIDAL"));
        var byDescription = await harness.Catalogue.SearchAsync(Query(search: "ceremony"));

        Assert.Equal("Bridal Day", Assert.Single(byName.Items).Name);
        Assert.Equal("Bridal Day", Assert.Single(byDescription.Items).Name);
    }

    [Fact]
    public async Task Search_applies_the_status_filter_to_bundles_too()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var haircutId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        var blowDryId = await harness.SeedServiceAsync(categoryId, "Blow Dry", 20m, staffIds: [staffId]);

        await harness.SeedBundleAsync(categoryId, "On the menu", 70m, [haircutId, blowDryId]);
        await harness.SeedBundleAsync(
            categoryId, "Taken off", 70m, [haircutId, blowDryId], isActive: false);

        var active = await harness.Catalogue.SearchAsync(Query(status: ServiceStatusFilter.Active));
        var inactive = await harness.Catalogue.SearchAsync(Query(status: ServiceStatusFilter.Inactive));

        Assert.Equal("On the menu", Assert.Single(active.Items, item => item.ItemKind == "Bundle").Name);
        Assert.Equal("Taken off", Assert.Single(inactive.Items).Name);
    }

    [Fact]
    public async Task Search_returns_only_the_bundles_that_may_be_booked_when_restricted()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var haircutId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        var blowDryId = await harness.SeedServiceAsync(categoryId, "Blow Dry", 20m, staffIds: [staffId]);
        var unstaffedId = await harness.SeedServiceAsync(categoryId, "Aromatherapy", 70m);

        await harness.SeedBundleAsync(categoryId, "Ready to book", 70m, [haircutId, blowDryId]);
        await harness.SeedBundleAsync(
            categoryId, "Switched off", 70m, [haircutId, blowDryId], isActive: false);
        await harness.SeedBundleAsync(categoryId, "Nobody can do it", 100m, [haircutId, unstaffedId]);

        var page = await harness.Catalogue.SearchAsync(Query(bookableOnly: true));

        Assert.Equal(
            new[] { "Blow Dry", "Haircut", "Ready to book" },
            page.Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task Search_pages_across_services_and_bundles_together()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var alphaId = await harness.SeedServiceAsync(categoryId, "Alpha", 60m, staffIds: [staffId]);
        var echoId = await harness.SeedServiceAsync(categoryId, "Echo", 40m, staffIds: [staffId]);

        await harness.SeedBundleAsync(categoryId, "Bravo", 90m, [alphaId, echoId]);
        await harness.SeedBundleAsync(categoryId, "Charlie", 90m, [alphaId, echoId]);
        await harness.SeedBundleAsync(categoryId, "Delta", 90m, [alphaId, echoId]);

        var firstPage = await harness.Catalogue.SearchAsync(Query(page: 1, pageSize: 2));

        Assert.Equal(5, firstPage.TotalCount);
        Assert.Equal(3, firstPage.TotalPages);

        // A bundle belongs in the middle of the menu, not after every service.
        Assert.Equal(
            new[] { "Alpha", "Bravo" },
            firstPage.Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task GetBookable_leaves_bundles_to_their_own_list()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var haircutId = await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        var blowDryId = await harness.SeedServiceAsync(categoryId, "Blow Dry", 20m, staffIds: [staffId]);

        await harness.SeedBundleAsync(categoryId, "Bridal Day", 70m, [haircutId, blowDryId]);

        var bookable = await harness.Catalogue.GetBookableAsync();

        Assert.Equal(
            new[] { "Blow Dry", "Haircut" },
            bookable.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task GetBookable_returns_only_services_that_can_actually_be_booked()
    {
        var (harness, categoryId, staffId) = await ArrangeAsync();
        using var _ = harness;

        var retiredCategory = await harness.SeedCategoryAsync("Retired", sortOrder: 5, isActive: false);
        var onLeave = await harness.SeedStaffAsync("Sonal", "Kapoor", isActive: false);

        await harness.SeedServiceAsync(categoryId, "Haircut", 60m, staffIds: [staffId]);
        await harness.SeedServiceAsync(categoryId, "Retired perm", 80m, isActive: false, staffIds: [staffId]);
        await harness.SeedServiceAsync(categoryId, "Unstaffed special", 80m);
        await harness.SeedServiceAsync(categoryId, "Only inactive staff", 80m, staffIds: [onLeave]);
        await harness.SeedServiceAsync(retiredCategory, "In a retired category", 80m, staffIds: [staffId]);

        var bookable = await harness.Catalogue.GetBookableAsync();

        var single = Assert.Single(bookable);

        Assert.Equal("Haircut", single.Name);
        Assert.True(single.IsBookable);
        Assert.Equal("Jasmin", Assert.Single(single.EligibleStaff).FirstName);
    }

    [Fact]
    public async Task GetSelectableStaff_hides_inactive_members_until_they_are_asked_for()
    {
        var (harness, _, _) = await ArrangeAsync();
        using var _2 = harness;

        await harness.SeedStaffAsync("Erica", "Mae");
        await harness.SeedStaffAsync("Sonal", "Kapoor", isActive: false);

        var selectable = await harness.Catalogue.GetSelectableStaffAsync(includeInactive: false);
        var everyone = await harness.Catalogue.GetSelectableStaffAsync(includeInactive: true);

        Assert.Equal(new[] { "Erica Mae", "Jasmin Rae" }, selectable.Select(member => member.DisplayName).ToArray());
        Assert.Equal(3, everyone.Count);
        Assert.Contains(everyone, member => member is { FirstName: "Sonal", IsActive: false });
    }
}
