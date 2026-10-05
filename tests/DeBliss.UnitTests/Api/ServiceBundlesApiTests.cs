using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for service bundles. The derivation and validation
/// rules are covered by <see cref="ServiceCatalogue.ServiceBundleServiceTests"/>; these
/// tests exist to catch anything lost between the controller and the wire — the bookable
/// filter, the JSON shape the React menu consumes, and the problem documents.
/// </summary>
public sealed class ServiceBundlesApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/service-bundles";
    private const string StaffEmail = "bundle-staff@debliss.test";
    private const string StaffPassword = "Test#Team123";

    private static SaveBundleRequest Request(
        Guid? categoryId,
        string? name = "Makeover",
        string? description = null,
        decimal price = 100m,
        bool isActive = true,
        int sortOrder = 0,
        IReadOnlyList<Guid>? items = null) =>
        new(name, categoryId, description, price, isActive, sortOrder, items);

    [Fact]
    public async Task Bookable_lists_only_the_bundles_a_guest_could_actually_book()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        await Factory.SeedBundleAsync(categoryId, "Makeover", 100m, [cut, colour]);

        // A component nobody performs makes the whole offering unbookable, and a
        // deactivated bundle is off the menu — neither may reach a guest.
        var orphan = await Factory.SeedServiceAsync(categoryId, "Scalp Ritual", 30m);
        await Factory.SeedBundleAsync(categoryId, "Unstaffed Mix", 60m, [orphan, cut]);
        await Factory.SeedBundleAsync(categoryId, "Retired", 90m, [cut, colour], isActive: false);

        var response = await Client.GetAsync($"{BaseUrl}/bookable");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var only = Assert.Single(await ReadAsync<IReadOnlyList<BundleDto>>(response));

        Assert.Equal("Makeover", only.Name);
        Assert.Equal("Hair", only.CategoryName);
        Assert.Equal(105, only.DurationMinutes);
        Assert.Equal(100m, only.Price);
        Assert.Equal(120m, only.StandardTotalPrice);
        Assert.Equal(20m, only.SavingsAmount);
        Assert.True(only.IsBookable);
        Assert.Equal(["Cut", "Colour"], only.Items.Select(item => item.Name).ToArray());
    }

    [Fact]
    public async Task GetById_returns_the_components_in_their_delivery_sequence_not_the_menu_order()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        // Cut sits first on the menu but the colour goes on first, so the two orders differ.
        var id = await Factory.SeedBundleAsync(
            categoryId, "Makeover", 100m, [colour, cut], description: "Colour, then the cut");

        var response = await Client.GetAsync($"{BaseUrl}/{id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bundle = await ReadAsync<BundleDto>(response);

        Assert.Equal(id, bundle.Id);
        Assert.Equal("Makeover", bundle.Name);
        Assert.Equal("Colour, then the cut", bundle.Description);
        Assert.Equal(categoryId, bundle.CategoryId);
        Assert.Equal("Hair", bundle.CategoryName);
        Assert.True(bundle.IsCategoryActive);
        Assert.Equal(105, bundle.DurationMinutes);
        Assert.Equal(120m, bundle.StandardTotalPrice);
        Assert.Equal(20m, bundle.SavingsAmount);
        Assert.True(bundle.IsActive);
        Assert.True(bundle.IsBookable);

        Assert.Equal(
            [("Colour", 0), ("Cut", 1)],
            bundle.Items.Select(item => (item.Name, item.DisplayOrder)).ToArray());
    }

    [Fact]
    public async Task GetById_answers_a_problem_document_for_an_unknown_id()
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
    public async Task Create_stores_the_bundle_with_the_submitted_delivery_sequence()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, description: "  Two hours, one price  ", items: [colour, cut]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<BundleDto>(response);

        Assert.Equal($"{BaseUrl}/{created.Id}", LocationPathOf(response));
        Assert.Equal("Makeover", created.Name);
        Assert.Equal("Two hours, one price", created.Description);
        Assert.Equal("Hair", created.CategoryName);
        Assert.Equal(105, created.DurationMinutes);
        Assert.Equal(100m, created.Price);
        Assert.Equal(120m, created.StandardTotalPrice);
        Assert.Equal(20m, created.SavingsAmount);
        Assert.True(created.IsActive);
        // Bookability depends on each component's staff, which the component lookup
        // does not load — a true here means the response was built from a re-read.
        Assert.True(created.IsBookable);
        Assert.Null(created.UpdatedAt);
        Assert.Equal(
            [("Colour", 0), ("Cut", 1)],
            created.Items.Select(item => (item.Name, item.DisplayOrder)).ToArray());
    }

    [Fact]
    public async Task Create_accepts_an_active_bundle_that_is_not_yet_bookable()
    {
        var (categoryId, cut, _) = await SeededMenuAsync();
        var orphan = await Factory.SeedServiceAsync(categoryId, "Scalp Ritual", 30m);

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, price: 60m, items: [cut, orphan]));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<BundleDto>(response);

        Assert.True(created.IsActive);
        Assert.False(created.IsBookable);
        Assert.Empty(await ReadAsync<IReadOnlyList<BundleDto>>(await Client.GetAsync($"{BaseUrl}/bookable")));
    }

    [Fact]
    public async Task Create_reports_every_field_problem_at_once()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            new SaveBundleRequest(null, null, null, -1m, true, -1, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Equal(
            ["categoryId", "items", "name", "price", "sortOrder"],
            problem.Errors.Keys.Order(StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public async Task Create_rejects_a_bundle_of_one()
    {
        var (categoryId, cut, _) = await SeededMenuAsync();

        var response = await Client.PostAsJsonAsync(BaseUrl, Request(categoryId, price: 30m, items: [cut]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains(
            problem.Errors["items"],
            message => message.Contains("at least 2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_rejects_the_same_component_twice()
    {
        var (categoryId, cut, _) = await SeededMenuAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, price: 30m, items: [cut, cut]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        // Two messages land on `items`: the repeat itself, and the one distinct
        // service left behind being fewer than a bundle needs.
        Assert.Contains(
            problem.Errors["items"],
            message => message.Contains("more than once", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_rejects_a_price_above_the_combined_individual_price()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, price: 121m, items: [cut, colour]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains(
            problem.Errors["price"],
            message => message.Contains("120.00", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_within_the_same_category()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        await Factory.SeedBundleAsync(categoryId, "Makeover", 100m, [cut, colour]);

        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            Request(categoryId, name: "makeover", items: [cut, colour]));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(409, problem.Status);
        Assert.Contains("'makeover'", problem.Detail, StringComparison.Ordinal);
        Assert.Contains("'Hair'", problem.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Update_replaces_the_component_set_and_the_delivery_sequence()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        var massage = await Factory.SeedServiceAsync(categoryId, "Massage", 50m, durationMinutes: 30);
        var id = await Factory.SeedBundleAsync(categoryId, "Makeover", 100m, [cut, colour]);

        // Cut leaves the bundle, Massage joins it, and the sequence follows the request.
        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{id}",
            Request(categoryId, name: "Refresh", price: 120m, items: [massage, colour]));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<BundleDto>(response);

        Assert.Equal(id, updated.Id);
        Assert.Equal("Refresh", updated.Name);
        Assert.Equal(120m, updated.Price);
        Assert.Equal(90, updated.DurationMinutes);
        Assert.Equal(130m, updated.StandardTotalPrice);
        Assert.Equal(10m, updated.SavingsAmount);
        Assert.NotNull(updated.UpdatedAt);
        Assert.Equal(
            [(massage, "Massage", 0), (colour, "Colour", 1)],
            updated.Items.Select(item => (item.ServiceId, item.Name, item.DisplayOrder)).ToArray());
    }

    [Fact]
    public async Task Update_answers_a_problem_document_for_an_unknown_id()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{Guid.NewGuid()}",
            Request(categoryId, items: [cut, colour]));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }

    [Fact]
    public async Task Set_status_deactivates_a_bundle_and_drops_it_from_the_bookable_list()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        var id = await Factory.SeedBundleAsync(categoryId, "Makeover", 100m, [cut, colour]);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{id}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bundle = await ReadAsync<BundleDto>(response);

        Assert.False(bundle.IsActive);
        Assert.False(bundle.IsBookable);
        Assert.Empty(await ReadAsync<IReadOnlyList<BundleDto>>(await Client.GetAsync($"{BaseUrl}/bookable")));
    }

    [Fact]
    public async Task Set_status_reactivates_a_bundle_whose_components_can_all_be_booked()
    {
        var (categoryId, cut, colour) = await SeededMenuAsync();
        var id = await Factory.SeedBundleAsync(categoryId, "Makeover", 100m, [cut, colour], isActive: false);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{id}/status", new SetStatusRequest(true));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bundle = await ReadAsync<BundleDto>(response);

        Assert.True(bundle.IsActive);
        Assert.True(bundle.IsBookable);
        Assert.Equal(id, Assert.Single(await ReadAsync<IReadOnlyList<BundleDto>>(
            await Client.GetAsync($"{BaseUrl}/bookable"))).Id);
    }

    [Fact]
    public async Task Set_status_refuses_to_activate_a_bundle_with_a_component_nobody_can_perform()
    {
        var (categoryId, cut, _) = await SeededMenuAsync();
        var orphan = await Factory.SeedServiceAsync(categoryId, "Scalp Ritual", 30m);
        var id = await Factory.SeedBundleAsync(categoryId, "Unstaffed Mix", 60m, [cut, orphan], isActive: false);

        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{id}/status", new SetStatusRequest(true));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains(
            problem.Errors["isActive"],
            message => message.Contains("Every service in the bundle", StringComparison.Ordinal));

        // The refused activation wrote nothing.
        Assert.False((await ReadAsync<BundleDto>(await Client.GetAsync($"{BaseUrl}/{id}"))).IsActive);
    }

    [Fact]
    public async Task Set_status_answers_a_problem_document_for_an_unknown_id()
    {
        var response = await Client.PatchAsJsonAsync($"{BaseUrl}/{Guid.NewGuid()}/status", new SetStatusRequest(false));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(404, (await ReadAsync<Problem>(response)).Status);
    }

    [Fact]
    public async Task A_staff_member_cannot_reach_the_bundle_catalogue()
    {
        await Factory.SeedUserAsync(StaffEmail, StaffPassword, RoleNames.Staff, "Aisha", "Stylist");

        using var client = AnonymousClient;
        await AuthenticateAsync(client, StaffEmail, StaffPassword);

        var list = await client.GetAsync($"{BaseUrl}/bookable");
        var create = await client.PostAsJsonAsync(BaseUrl, Request(Guid.NewGuid()));

        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    /// <summary>
    /// A two-service menu with one stylist who can perform both: Cut (45 min, 40)
    /// first on the menu, Colour (60 min, 80) second, so together they make a
    /// 105-minute, 120 offering.
    /// </summary>
    private async Task<(Guid CategoryId, Guid Cut, Guid Colour)> SeededMenuAsync()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var cut = await Factory.SeedServiceAsync(categoryId, "Cut", 40m, durationMinutes: 45, staffIds: [ana]);
        var colour = await Factory.SeedServiceAsync(
            categoryId, "Colour", 80m, durationMinutes: 60, staffIds: [ana], sortOrder: 1);

        return (categoryId, cut, colour);
    }
}
