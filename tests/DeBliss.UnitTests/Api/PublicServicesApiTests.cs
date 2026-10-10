using System.Net;
using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the anonymous customer-facing service menu over the real HTTP
/// pipeline: what a browser with no session would receive. The filtering rule
/// itself is covered by <see cref="ServiceCatalogue.ServiceCatalogueServiceTests"/>;
/// these tests exist for anonymous access, the wire shape and the ordering.
/// </summary>
public sealed class PublicServicesApiTests : ApiTestBase
{
    private const string Url = "/api/public/services";

    [Fact]
    public async Task The_menu_answers_anonymous_traffic_without_credentials()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m, isOnlineBookable: true);

        var response = await AnonymousClient.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var services = await ReadAsync<IReadOnlyList<PublicServiceDto>>(response);

        Assert.Equal("Cut", Assert.Single(services).Name);
    }

    [Fact]
    public async Task The_menu_offers_only_active_online_bookable_services_in_active_categories()
    {
        var hair = await Factory.SeedCategoryAsync("Hair");
        var spa = await Factory.SeedCategoryAsync("Spa", isActive: false);

        await Factory.SeedServiceAsync(hair, "Cut", 40m, isOnlineBookable: true);
        await Factory.SeedServiceAsync(hair, "Colour", 80m, isOnlineBookable: true, isActive: false);
        await Factory.SeedServiceAsync(hair, "Trim", 20m);
        await Factory.SeedServiceAsync(spa, "Massage", 90m, isOnlineBookable: true);

        var services = await ReadAsync<IReadOnlyList<PublicServiceDto>>(await AnonymousClient.GetAsync(Url));

        Assert.Equal(["Cut"], services.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task The_menu_carries_only_the_customer_facing_fields()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair", sortOrder: 3);
        var serviceId = await Factory.SeedServiceAsync(
            categoryId,
            "Cut",
            40.55m,
            durationMinutes: 45,
            description: "Ladies cut",
            isOnlineBookable: true,
            sortOrder: 2);

        var service = Assert.Single(await ReadAsync<IReadOnlyList<PublicServiceDto>>(
            await AnonymousClient.GetAsync(Url)));

        Assert.Equal(serviceId, service.Id);
        Assert.Equal("Cut", service.Name);
        Assert.Equal("Ladies cut", service.Description);
        Assert.Equal(categoryId, service.CategoryId);
        Assert.Equal("Hair", service.CategoryName);
        Assert.Equal(45, service.DurationMinutes);
        Assert.Equal(40.55m, service.Price);
        Assert.True(service.IsOnlineBookable);
        Assert.Equal(2, service.SortOrder);
    }

    [Fact]
    public async Task The_menu_orders_by_category_position_then_service_position_then_name()
    {
        var second = await Factory.SeedCategoryAsync("Hair", sortOrder: 2);
        var first = await Factory.SeedCategoryAsync("Nails", sortOrder: 1);

        await Factory.SeedServiceAsync(second, "Zebra cut", 40m, isOnlineBookable: true, sortOrder: 0);
        await Factory.SeedServiceAsync(second, "Cut", 40m, isOnlineBookable: true, sortOrder: 1);
        await Factory.SeedServiceAsync(second, "Colour", 80m, isOnlineBookable: true, sortOrder: 1);
        await Factory.SeedServiceAsync(first, "Manicure", 30m, isOnlineBookable: true, sortOrder: 5);

        var services = await ReadAsync<IReadOnlyList<PublicServiceDto>>(await AnonymousClient.GetAsync(Url));

        Assert.Equal(
            ["Manicure", "Zebra cut", "Colour", "Cut"],
            services.Select(service => service.Name).ToArray());
    }

    [Fact]
    public async Task The_menu_answers_an_empty_array_when_nothing_is_offered_online()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(categoryId, "Trim", 20m);

        var response = await AnonymousClient.GetAsync(Url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadAsync<IReadOnlyList<PublicServiceDto>>(response));
    }

    [Fact]
    public async Task The_public_menu_stays_anonymous_while_staff_endpoints_still_refuse_anonymous_traffic()
    {
        var anonymous = AnonymousClient;

        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync(Url)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/services")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync("/api/staff")).StatusCode);
    }
}
