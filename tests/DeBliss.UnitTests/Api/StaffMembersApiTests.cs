using System.Net;
using DeBliss.Application.ServiceCatalogue.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the read-only staff lookup that feeds the eligible-staff selector.
/// </summary>
public sealed class StaffMembersApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/staff-members";

    [Fact]
    public async Task Get_offers_active_staff_in_display_order()
    {
        await Factory.SeedStaffAsync("Ben", "Osei");
        await Factory.SeedStaffAsync("Ana", "Bianchi");
        await Factory.SeedStaffAsync("Zoe", "Rider", isActive: false);

        var response = await Client.GetAsync(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var staff = await ReadAsync<IReadOnlyList<StaffOptionDto>>(response);

        Assert.Equal(["Ana Bianchi", "Ben Osei"], staff.Select(member => member.DisplayName).ToArray());
        Assert.All(staff, member => Assert.True(member.IsActive));
    }

    [Fact]
    public async Task Get_can_include_inactive_staff_so_an_existing_assignment_still_renders()
    {
        await Factory.SeedStaffAsync("Ana", "Bianchi");
        await Factory.SeedStaffAsync("Zoe", "Rider", isActive: false);

        var staff = await ReadAsync<IReadOnlyList<StaffOptionDto>>(
            await Client.GetAsync($"{BaseUrl}?includeInactive=true"));

        Assert.Equal(2, staff.Count);
        Assert.False(staff.Single(member => member.FirstName == "Zoe").IsActive);
    }
}
