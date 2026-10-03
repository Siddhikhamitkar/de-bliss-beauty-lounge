using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Staffing;

public class StaffServiceTests
{
    private static SaveStaffRequest Request(
        string? firstName = "Jasmin",
        string? lastName = "Rae",
        string? email = null,
        string? phoneNumber = null,
        string? jobTitle = null,
        string? calendarColor = null,
        bool isActive = true,
        int sortOrder = 0) =>
        new(firstName, lastName, email, phoneNumber, jobTitle, calendarColor, isActive, sortOrder);

    private static StaffQuery Query(
        string? search = null,
        StaffStatusFilter status = StaffStatusFilter.All,
        int page = 1,
        int pageSize = 50) =>
        new(search, status, page, pageSize);

    [Fact]
    public async Task Create_saves_the_profile_with_its_contact_details()
    {
        using var harness = new StaffingHarness();

        var created = await harness.Staff.CreateAsync(Request(
            email: " jasmin@debliss.ae ",
            phoneNumber: "+971 50 000 0000",
            jobTitle: "Senior Stylist",
            calendarColor: "#E91E63",
            sortOrder: 3));

        Assert.Equal("Jasmin", created.FirstName);
        Assert.Equal("Rae", created.LastName);
        Assert.Equal("jasmin@debliss.ae", created.Email);
        Assert.Equal("+971 50 000 0000", created.PhoneNumber);
        Assert.Equal("Senior Stylist", created.JobTitle);
        Assert.Equal("#E91E63", created.CalendarColor);
        Assert.True(created.IsActive);
        Assert.Equal(3, created.SortOrder);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);
        Assert.Empty(created.Services);

        await using var verify = harness.Reopen();

        var stored = await verify.Staff.SingleAsync();

        Assert.Equal("jasmin@debliss.ae", stored.Email);
        Assert.Equal("Senior Stylist", stored.JobTitle);
    }

    [Theory]
    [InlineData(null, "Rae", "firstName")]
    [InlineData("", "Rae", "firstName")]
    [InlineData("   ", "Rae", "firstName")]
    [InlineData("Jasmin", null, "lastName")]
    [InlineData("Jasmin", "", "lastName")]
    public async Task Create_rejects_a_missing_name(string? firstName, string? lastName, string expectedKey)
    {
        using var harness = new StaffingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Staff.CreateAsync(Request(firstName, lastName)));

        Assert.Single(failure.Errors);
        Assert.Contains(expectedKey, failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_every_invalid_field_in_one_report()
    {
        using var harness = new StaffingHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Staff.CreateAsync(Request(
                firstName: null,
                email: "not-an-email",
                calendarColor: "red",
                sortOrder: -1)));

        Assert.Contains("firstName", failure.Errors.Keys);
        Assert.Contains("email", failure.Errors.Keys);
        Assert.Contains("calendarColor", failure.Errors.Keys);
        Assert.Contains("sortOrder", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_accepts_a_three_digit_colour()
    {
        using var harness = new StaffingHarness();

        var created = await harness.Staff.CreateAsync(Request(calendarColor: "#f5a"));

        Assert.Equal("#f5a", created.CalendarColor);
    }

    [Fact]
    public async Task Create_stores_blank_optional_fields_as_null()
    {
        using var harness = new StaffingHarness();

        var created = await harness.Staff.CreateAsync(Request(email: "  ", phoneNumber: "", jobTitle: " "));

        Assert.Null(created.Email);
        Assert.Null(created.PhoneNumber);
        Assert.Null(created.JobTitle);
    }

    [Fact]
    public async Task Update_persists_the_new_details()
    {
        using var harness = new StaffingHarness();

        var id = await harness.SeedStaffAsync("Jasmin", "Rae", jobTitle: "Stylist");

        var updated = await harness.Staff.UpdateAsync(id, Request(
            firstName: "Jasmin",
            lastName: "Ray",
            email: "jasmin.ray@debliss.ae",
            jobTitle: "Senior Stylist",
            calendarColor: "#3F51B5",
            sortOrder: 1));

        Assert.Equal("Ray", updated.LastName);
        Assert.Equal("jasmin.ray@debliss.ae", updated.Email);
        Assert.Equal("Senior Stylist", updated.JobTitle);
        Assert.NotNull(updated.UpdatedAt);

        await using var verify = harness.Reopen();

        var stored = await verify.Staff.SingleAsync();

        Assert.Equal("Ray", stored.LastName);
        Assert.Equal("#3F51B5", stored.CalendarColor);
    }

    [Fact]
    public async Task SetStatusAsync_flips_the_member_between_active_and_inactive()
    {
        using var harness = new StaffingHarness();

        var id = await harness.SeedStaffAsync("Jasmin", "Rae");

        var deactivated = await harness.Staff.SetStatusAsync(id, isActive: false);

        Assert.False(deactivated.IsActive);

        var reactivated = await harness.Staff.SetStatusAsync(id, isActive: true);

        Assert.True(reactivated.IsActive);

        await using var verify = harness.Reopen();

        Assert.True(await verify.Staff.Select(member => member.IsActive).SingleAsync());
    }

    [Fact]
    public async Task GetAsync_returns_404_for_an_unknown_member()
    {
        using var harness = new StaffingHarness();

        var missing = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<NotFoundException>(() => harness.Staff.GetAsync(missing));

        Assert.Contains(missing.ToString(), failure.Message);
    }

    private sealed record Fixture(StaffingHarness Harness, Guid StaffId, Guid FirstServiceId, Guid SecondServiceId, Guid ThirdServiceId);

    private static async Task<Fixture> ArrangeServicesAsync()
    {
        var harness = new StaffingHarness();
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        var first = await harness.SeedServiceAsync(categoryId, "Cut");
        var second = await harness.SeedServiceAsync(categoryId, "Colour");
        var third = await harness.SeedServiceAsync(categoryId, "Blow-dry");

        return new Fixture(harness, staffId, first, second, third);
    }

    [Fact]
    public async Task SetServicesAsync_replaces_the_whole_assignment_set()
    {
        var (harness, staffId, first, second, third) = await ArrangeServicesAsync();
        using var _ = harness;

        var initial = await harness.Staff.SetServicesAsync(staffId, new SetStaffServicesRequest([first, second]));

        Assert.Equal(2, initial.Count);

        var replaced = await harness.Staff.SetServicesAsync(staffId, new SetStaffServicesRequest([second, third]));

        Assert.Equal(
            new[] { second, third }.OrderBy(id => id),
            replaced.Select(service => service.ServiceId).OrderBy(id => id).ToArray());

        await using var verify = harness.Reopen();

        var kept = await verify.StaffServices.SingleAsync(line => line.ServiceId == second);

        Assert.Equal(initial.Single(service => service.ServiceId == second).ServiceId, kept.ServiceId);
        Assert.False(await verify.StaffServices.AnyAsync(line => line.ServiceId == first));
        Assert.Equal(2, await verify.StaffServices.CountAsync());
    }

    [Fact]
    public async Task SetServicesAsync_dedupes_repeated_and_empty_ids()
    {
        var (harness, staffId, first, _, _) = await ArrangeServicesAsync();
        using var _ = harness;

        var assigned = await harness.Staff.SetServicesAsync(
            staffId,
            new SetStaffServicesRequest([first, first, Guid.Empty, first]));

        var only = Assert.Single(assigned);

        Assert.Equal(first, only.ServiceId);

        await using var verify = harness.Reopen();

        Assert.Equal(1, await verify.StaffServices.CountAsync());
    }

    [Fact]
    public async Task SetServicesAsync_rejects_a_service_that_no_longer_exists()
    {
        var (harness, staffId, first, _, _) = await ArrangeServicesAsync();
        using var _ = harness;

        var ghost = Guid.NewGuid();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Staff.SetServicesAsync(staffId, new SetStaffServicesRequest([first, ghost])));

        Assert.Contains("serviceIds", failure.Errors.Keys);
    }

    [Fact]
    public async Task SetServicesAsync_returns_404_for_an_unknown_member()
    {
        using var harness = new StaffingHarness();

        var failure = await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Staff.SetServicesAsync(Guid.NewGuid(), new SetStaffServicesRequest([])));

        Assert.Contains("was not found", failure.Message);
    }

    [Fact]
    public async Task GetServicesAsync_lists_what_the_member_can_perform()
    {
        var (harness, staffId, first, second, _) = await ArrangeServicesAsync();
        using var _ = harness;

        await harness.Staff.SetServicesAsync(staffId, new SetStaffServicesRequest([first, second]));

        var services = await harness.Staff.GetServicesAsync(staffId);

        Assert.Equal(["Colour", "Cut"], services.Select(service => service.Name).Order().ToArray());
        Assert.All(services, service => Assert.Equal("Hair", service.CategoryName));
    }

    [Fact]
    public async Task Search_pages_the_members_and_counts_the_total()
    {
        using var harness = new StaffingHarness();

        await harness.SeedStaffAsync("Ana", "Bianchi");
        await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.SeedStaffAsync("Mira", "Said");

        var page = await harness.Staff.SearchAsync(Query(page: 2, pageSize: 2));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.TotalPages);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Search_matches_the_term_against_names_and_job_titles()
    {
        using var harness = new StaffingHarness();

        await harness.SeedStaffAsync("Jasmin", "Rae", jobTitle: "Senior Stylist");
        await harness.SeedStaffAsync("Mira", "Said", jobTitle: "Nail Technician");
        await harness.SeedStaffAsync("Ana", "Bianchi", email: "ana@debliss.ae");

        var byName = await harness.Staff.SearchAsync(Query(search: "said"));
        var byRole = await harness.Staff.SearchAsync(Query(search: "stylist"));
        var byEmail = await harness.Staff.SearchAsync(Query(search: "ana@"));

        Assert.Equal("Mira", Assert.Single(byName.Items).FirstName);
        Assert.Equal("Jasmin", Assert.Single(byRole.Items).FirstName);
        Assert.Equal("Ana", Assert.Single(byEmail.Items).FirstName);
    }

    [Fact]
    public async Task Search_narrows_by_record_state()
    {
        using var harness = new StaffingHarness();

        await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.SeedStaffAsync("Mira", "Said", isActive: false);

        var active = await harness.Staff.SearchAsync(Query(status: StaffStatusFilter.Active));
        var inactive = await harness.Staff.SearchAsync(Query(status: StaffStatusFilter.Inactive));

        Assert.Equal("Jasmin", Assert.Single(active.Items).FirstName);
        Assert.Equal("Mira", Assert.Single(inactive.Items).FirstName);
    }

    [Fact]
    public async Task Search_counts_only_active_services_in_the_summary()
    {
        var (harness, staffId, first, second, _) = await ArrangeServicesAsync();
        using var _ = harness;

        await harness.SeedAssignmentAsync(staffId, first);
        await harness.SeedAssignmentAsync(staffId, second);

        await using var setup = harness.Reopen();

        var retired = await setup.Services.SingleAsync(service => service.Id == second);

        retired.IsActive = false;
        await setup.SaveChangesAsync();

        harness.Context.ChangeTracker.Clear();

        var page = await harness.Staff.SearchAsync(Query());

        Assert.Equal(1, Assert.Single(page.Items).ServiceCount);
    }
}
