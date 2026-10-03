using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.Common.Paging;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for the bookable service menu. The rules are
/// covered by <see cref="ServiceCatalogue.ServiceCatalogueServiceTests"/>; these
/// tests exist to catch anything lost between the controller and the wire — query
/// string binding, paging metadata, the JSON shape the React app will consume.
/// </summary>
public sealed class ServicesApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/services";

    private static SaveServiceRequest Request(
        Guid? categoryId,
        string? name = "Cut",
        string? description = null,
        int durationMinutes = 30,
        decimal price = 40m,
        bool isActive = true,
        bool isOnlineBookable = false,
        int sortOrder = 0,
        IReadOnlyList<Guid>? eligibleStaffIds = null) =>
        new(name, categoryId, description, durationMinutes, price, isActive, isOnlineBookable, sortOrder, eligibleStaffIds);

    [Fact]
    public async Task Search_returns_one_page_of_summaries_with_enough_metadata_to_render_a_pager()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(categoryId, "Blow-dry", 25m);
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m);

        var response = await Client.GetAsync($"{BaseUrl}?page=2&pageSize=1");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(response);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(1, page.PageSize);
        Assert.Equal(2, page.TotalPages);
        Assert.True(page.HasPreviousPage);
        Assert.False(page.HasNextPage);

        var only = Assert.Single(page.Items);

        Assert.Equal("Cut", only.Name);
        Assert.Equal("Hair", only.CategoryName);
        Assert.Equal(40m, only.Price);
        Assert.Equal(0, only.EligibleStaffCount);
        Assert.False(only.IsBookable);
    }

    [Fact]
    public async Task Search_orders_by_category_position_then_by_the_service_own_position()
    {
        var second = await Factory.SeedCategoryAsync("Hair", sortOrder: 2);
        var first = await Factory.SeedCategoryAsync("Nails", sortOrder: 1);
        await Factory.SeedServiceAsync(second, "Cut", 40m, sortOrder: 1);
        await Factory.SeedServiceAsync(second, "Colour", 80m, sortOrder: 0);
        await Factory.SeedServiceAsync(first, "Manicure", 30m);

        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(await Client.GetAsync(BaseUrl));

        Assert.Equal(
            ["Manicure", "Colour", "Cut"],
            page.Items.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task Search_clamps_an_absurd_page_request_instead_of_reading_the_whole_table()
    {
        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?page=0&pageSize=100000"));

        Assert.Equal(1, page.Page);
        Assert.Equal(ServiceQuery.MaximumPageSize, page.PageSize);
    }

    [Fact]
    public async Task Search_matches_the_term_against_the_name_and_the_description()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m);
        await Factory.SeedServiceAsync(categoryId, "Colour", 80m, description: "Full head highlights");

        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=HIGHlights"));

        Assert.Equal("Colour", Assert.Single(page.Items).Name);
        Assert.Equal(1, page.TotalCount);
    }

    [Fact]
    public async Task Search_can_be_narrowed_to_a_single_category()
    {
        var hair = await Factory.SeedCategoryAsync("Hair");
        var nails = await Factory.SeedCategoryAsync("Nails");
        await Factory.SeedServiceAsync(hair, "Cut", 40m);
        await Factory.SeedServiceAsync(nails, "Manicure", 30m);

        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?categoryId={nails}"));

        Assert.Equal("Manicure", Assert.Single(page.Items).Name);
    }

    [Fact]
    public async Task Search_can_be_narrowed_to_services_that_can_actually_be_booked()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m, staffIds: [staffId]);
        await Factory.SeedServiceAsync(categoryId, "Trim", 20m);

        var page = await ReadAsync<PagedResult<ServiceSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?bookableOnly=true"));

        var only = Assert.Single(page.Items);

        Assert.Equal("Cut", only.Name);
        Assert.True(only.IsBookable);
        Assert.Equal(1, only.EligibleStaffCount);
    }

    [Fact]
    public async Task Bookable_offers_only_services_that_are_active_in_an_active_category_with_an_active_member_of_staff()
    {
        var hair = await Factory.SeedCategoryAsync("Hair");
        var spa = await Factory.SeedCategoryAsync("Spa", isActive: false);
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var ben = await Factory.SeedStaffAsync("Ben", "Osei", isActive: false);

        await Factory.SeedServiceAsync(hair, "Cut", 40m, staffIds: [ana]);
        await Factory.SeedServiceAsync(hair, "Colour", 80m, staffIds: [ben]);
        await Factory.SeedServiceAsync(hair, "Trim", 20m);
        await Factory.SeedServiceAsync(hair, "Wash", 15m, isActive: false, staffIds: [ana]);
        await Factory.SeedServiceAsync(spa, "Massage", 90m, staffIds: [ana]);

        var response = await Client.GetAsync($"{BaseUrl}/bookable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bookable = await ReadAsync<IReadOnlyList<ServiceDto>>(response);

        Assert.Equal(["Cut"], bookable.Select(service => service.Name).ToArray());
        Assert.True(Assert.Single(bookable).IsBookable);
    }

    [Fact]
    public async Task Get_by_id_returns_the_service_with_its_eligible_staff_in_name_order()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var ben = await Factory.SeedStaffAsync("Ben", "Osei");
        var serviceId = await Factory.SeedServiceAsync(
            categoryId, "Cut", 40.55m, durationMinutes: 45, description: "Ladies cut", staffIds: [ben, ana]);

        var response = await Client.GetAsync($"{BaseUrl}/{serviceId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var service = await ReadAsync<ServiceDto>(response);

        Assert.Equal(serviceId, service.Id);
        Assert.Equal("Cut", service.Name);
        Assert.Equal("Ladies cut", service.Description);
        Assert.Equal(45, service.DurationMinutes);
        Assert.Equal(40.55m, service.Price);
        Assert.Equal(categoryId, service.CategoryId);
        Assert.Equal("Hair", service.CategoryName);
        Assert.True(service.IsCategoryActive);
        Assert.True(service.IsBookable);
        Assert.Equal(["Ana", "Ben"], service.EligibleStaff.Select(member => member.FirstName).ToArray());
    }

    [Fact]
    public async Task Get_by_id_answers_a_problem_document_for_an_unknown_id()
    {
        var id = Guid.NewGuid();

        var response = await Client.GetAsync($"{BaseUrl}/{id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(404, problem.Status);
        Assert.Equal($"{BaseUrl}/{id}", problem.Instance);
        Assert.Contains(id.ToString(), problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_stores_the_service_and_the_staff_assigned_to_it()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, description: "  Ladies cut  ", price: 40.5m, eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<ServiceDto>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Cut", created.Name);
        Assert.Equal("Ladies cut", created.Description);
        Assert.Equal(40.5m, created.Price);
        Assert.Equal(categoryId, created.CategoryId);
        Assert.Equal("Hair", created.CategoryName);
        Assert.True(created.IsActive);
        Assert.True(created.IsBookable);
        Assert.Null(created.UpdatedAt);
        Assert.Equal(ana, Assert.Single(created.EligibleStaff).StaffId);

        Assert.Equal($"{BaseUrl}/{created.Id}", LocationPathOf(response));
    }

    [Fact]
    public async Task Create_reports_every_validation_problem_in_one_response()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            new SaveServiceRequest(null, null, null, 0, -1m, true, false, -1, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Equal(
            ["categoryId", "durationMinutes", "eligibleStaffIds", "name", "price", "sortOrder"],
            problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Theory]
    [InlineData("", "name")]
    [InlineData("   ", "name")]
    public async Task Create_rejects_a_missing_name(string name, string errorKey)
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, name, eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(errorKey, (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_duration_longer_than_a_day()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, durationMinutes: ServiceCatalogueLimits.MaximumDurationMinutes + 1, eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("durationMinutes", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_price_with_more_than_two_decimal_places()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, price: 40.555m, eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("price", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_an_active_service_that_nobody_can_perform()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ben = await Factory.SeedStaffAsync("Ben", "Osei", isActive: false);

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(categoryId, eligibleStaffIds: [ben]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("eligibleStaffIds", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_accepts_an_inactive_service_that_nobody_can_perform_yet()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(categoryId, isActive: false));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<ServiceDto>(response);

        Assert.False(created.IsActive);
        Assert.False(created.IsBookable);
        Assert.Empty(created.EligibleStaff);
    }

    [Fact]
    public async Task Create_rejects_a_staff_member_that_no_longer_exists()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, eligibleStaffIds: [Guid.NewGuid(), Guid.NewGuid()]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("eligibleStaffIds", problem.Errors.Keys);
        Assert.Contains(
            problem.Errors["eligibleStaffIds"],
            message => message.Contains("2 of the selected", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_rejects_a_category_that_no_longer_exists()
    {
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(Guid.NewGuid(), eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("categoryId", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_within_the_same_category()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m, staffIds: [ana]);

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(categoryId, name: "cut", eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(409, problem.Status);
        Assert.Contains("'cut'", problem.Detail, StringComparison.Ordinal);
        Assert.Contains("'Hair'", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_allows_the_same_name_in_a_different_category()
    {
        var hair = await Factory.SeedCategoryAsync("Hair");
        var nails = await Factory.SeedCategoryAsync("Nails");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        await Factory.SeedServiceAsync(hair, "Cut", 40m, staffIds: [ana]);

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(nails, name: "Cut", eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Update_replaces_the_whole_staff_assignment_set()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var ben = await Factory.SeedStaffAsync("Ben", "Osei");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, staffIds: [ana]);

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{serviceId}",
            Request(categoryId, price: 45m, isOnlineBookable: true, eligibleStaffIds: [ben]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<ServiceDto>(response);

        Assert.Equal(serviceId, updated.Id);
        Assert.Equal(45m, updated.Price);
        Assert.True(updated.IsOnlineBookable);
        Assert.Equal(ben, Assert.Single(updated.EligibleStaff).StaffId);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_answers_a_problem_document_for_an_unknown_id()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}",
            Request(categoryId, eligibleStaffIds: [ana]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }

    [Fact]
    public async Task Update_reports_validation_problems_before_it_reports_a_missing_service()
    {
        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}",
            Request(null, name: "  ", eligibleStaffIds: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("name", problem.Errors.Keys);
        Assert.Contains("categoryId", problem.Errors.Keys);
    }

    [Fact]
    public async Task Set_status_deactivates_a_service_and_drops_it_from_the_bookable_list()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, staffIds: [ana]);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{serviceId}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var service = await ReadAsync<ServiceDto>(response);

        Assert.False(service.IsActive);
        Assert.False(service.IsBookable);
        Assert.Equal(ana, Assert.Single(service.EligibleStaff).StaffId);

        var bookable = await ReadAsync<IReadOnlyList<ServiceDto>>(await Client.GetAsync($"{BaseUrl}/bookable"));

        Assert.Empty(bookable);
    }

    [Fact]
    public async Task Set_status_reactivates_a_service_once_somebody_can_perform_it_again()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, isActive: false, staffIds: [ana]);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{serviceId}/status", new SetStatusRequest(true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True((await ReadAsync<ServiceDto>(response)).IsActive);
    }

    [Fact]
    public async Task Set_status_refuses_to_reactivate_a_service_nobody_can_perform()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, isActive: false);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{serviceId}/status", new SetStatusRequest(true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Contains("eligibleStaffIds", problem.Errors.Keys);
    }

    [Fact]
    public async Task Set_status_answers_a_problem_document_for_an_unknown_id()
    {
        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }
}
