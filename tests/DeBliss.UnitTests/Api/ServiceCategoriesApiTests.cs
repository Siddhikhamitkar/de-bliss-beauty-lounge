using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.ServiceCatalogue.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline — routing, enum and query-string binding, the
/// exception handler, the JSON serializer — over a throwaway database. The rules
/// themselves are covered by
/// <see cref="ServiceCatalogue.ServiceCategoryServiceTests"/>; these tests exist to
/// catch anything lost between the controller and the wire.
/// </summary>
public sealed class ServiceCategoriesApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/service-categories";

    [Fact]
    public async Task Get_returns_every_category_in_display_order_with_its_service_count()
    {
        var hair = await Factory.SeedCategoryAsync("Hair", sortOrder: 1);
        var nails = await Factory.SeedCategoryAsync("Nails", sortOrder: 0);
        await Factory.SeedServiceAsync(hair, "Cut", 40m);

        var response = await Client.GetAsync(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);

        var categories = await ReadAsync<IReadOnlyList<ServiceCategoryDto>>(response);

        Assert.Equal(["Nails", "Hair"], categories.Select(category => category.Name).ToArray());
        Assert.Equal(1, categories.Single(category => category.Id == hair).ServiceCount);
        Assert.Equal(0, categories.Single(category => category.Id == nails).ServiceCount);
    }

    [Theory]
    [InlineData("Active", "Hair")]
    [InlineData("Inactive", "Spa")]
    public async Task Get_narrows_to_one_state_by_name(string status, string expectedName)
    {
        await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedCategoryAsync("Spa", isActive: false);

        var response = await Client.GetAsync($"{BaseUrl}?status={status}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var categories = await ReadAsync<IReadOnlyList<ServiceCategoryDto>>(response);

        Assert.Equal([expectedName], categories.Select(category => category.Name).ToArray());
    }

    [Fact]
    public async Task Get_by_id_returns_the_category()
    {
        var id = await Factory.SeedCategoryAsync("Hair", description: "Cut and colour", sortOrder: 4);

        var response = await Client.GetAsync($"{BaseUrl}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var category = await ReadAsync<ServiceCategoryDto>(response);

        Assert.Equal(id, category.Id);
        Assert.Equal("Hair", category.Name);
        Assert.Equal("Cut and colour", category.Description);
        Assert.Equal(4, category.SortOrder);
        Assert.True(category.IsActive);
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
    public async Task Get_by_id_does_not_match_a_route_for_an_id_that_is_not_a_guid()
    {
        var response = await Client.GetAsync($"{BaseUrl}/not-a-guid");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Create_returns_the_new_category_and_a_location_that_reads_it_back()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            new SaveServiceCategoryRequest("  Hair  ", "Cut and colour", 3, true));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<ServiceCategoryDto>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Hair", created.Name);
        Assert.Equal("Cut and colour", created.Description);
        Assert.Equal(3, created.SortOrder);
        Assert.Equal(0, created.ServiceCount);

        var location = LocationPathOf(response);

        Assert.Equal($"{BaseUrl}/{created.Id}", location);

        var readBack = await Client.GetAsync(location);

        Assert.Equal(HttpStatusCode.OK, readBack.StatusCode);
        Assert.Equal(created.Id, (await ReadAsync<ServiceCategoryDto>(readBack)).Id);
    }

    [Fact]
    public async Task Create_reports_every_validation_problem_in_one_response()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            new SaveServiceCategoryRequest(
                "   ",
                new string('x', ServiceCatalogueLimits.CategoryDescriptionMaxLength + 1),
                -1,
                true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Equal(
            ["description", "name", "sortOrder"],
            problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Create_rejects_a_name_longer_than_the_column()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            new SaveServiceCategoryRequest(new string('x', ServiceCatalogueLimits.CategoryNameMaxLength + 1), null, 0, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("name", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_regardless_of_case()
    {
        await Factory.SeedCategoryAsync("Hair");

        var response = await Client.PostAsJsonAsync(BaseUrl, new SaveServiceCategoryRequest("hair", null, 0, true));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(409, problem.Status);
        Assert.Contains("'hair'", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_before_it_reports_a_conflict_over_an_invalid_request()
    {
        await Factory.SeedCategoryAsync("Hair");

        var response = await Client.PostAsJsonAsync(BaseUrl, new SaveServiceCategoryRequest("Hair", null, -1, true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("sortOrder", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    [Fact]
    public async Task Update_changes_the_category_and_keeps_its_service_count()
    {
        var id = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(id, "Cut", 40m);

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{id}",
            new SaveServiceCategoryRequest("Hair & Beauty", null, 5, false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<ServiceCategoryDto>(response);

        Assert.Equal("Hair & Beauty", updated.Name);
        Assert.Null(updated.Description);
        Assert.Equal(5, updated.SortOrder);
        Assert.False(updated.IsActive);
        Assert.Equal(1, updated.ServiceCount);
        Assert.NotNull(updated.UpdatedAt);
    }

    [Fact]
    public async Task Update_answers_a_problem_document_for_an_unknown_id()
    {
        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}",
            new SaveServiceCategoryRequest("Hair", null, 0, true));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }

    [Fact]
    public async Task Set_status_deactivates_a_category_without_touching_the_services_underneath_it()
    {
        var id = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(id, "Cut", 40m);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{id}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var category = await ReadAsync<ServiceCategoryDto>(response);

        Assert.False(category.IsActive);
        Assert.Equal(1, category.ServiceCount);

        await using var verify = Factory.Database.CreateContext();

        var stored = await verify.Services.SingleAsync(service => service.Id == serviceId);

        Assert.True(stored.IsActive);
    }

    [Fact]
    public async Task Set_status_answers_a_problem_document_for_an_unknown_id()
    {
        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }
}
