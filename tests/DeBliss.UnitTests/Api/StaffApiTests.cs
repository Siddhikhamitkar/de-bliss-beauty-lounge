using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Staffing.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for staff profiles, schedules and availability.
/// The rules are covered by <see cref="Staffing.StaffServiceTests"/> and the other
/// staffing suites; these tests catch what the wire adds: query binding, the RFC
/// 9457 problem bodies, and the JSON shape the React app consumes.
/// </summary>
public sealed class StaffApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/staff";

    private static readonly DateTimeOffset MondayMorning =
        new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static object SaveRequest(
        string? firstName = "Jasmin",
        string? lastName = "Rae",
        string? email = "jasmin@debliss.ae",
        string? phoneNumber = "+971501234567",
        string? jobTitle = "Stylist",
        string? calendarColor = "#FF5733",
        bool isActive = true,
        int sortOrder = 0) =>
        new
        {
            firstName,
            lastName,
            email,
            phoneNumber,
            jobTitle,
            calendarColor,
            isActive,
            sortOrder
        };

    private static object WeekBody(
        string day = "Monday",
        string start = "09:00",
        string end = "17:00") =>
        new
        {
            days = new[]
            {
                new
                {
                    dayOfWeek = day,
                    isWorkingDay = true,
                    intervals = new[] { new { start, end } }
                }
            }
        };

    [Fact]
    public async Task Search_returns_paged_summaries_with_service_counts()
    {
        var ana = await Factory.SeedStaffAsync("Ana", "Bianchi", jobTitle: "Stylist");
        await Factory.SeedStaffAsync("Ben", "Osei", isActive: false);
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        await Factory.SeedServiceAsync(categoryId, "Cut", 40m, staffIds: [ana]);

        var response = await Client.GetAsync(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ReadAsync<PagedResult<StaffSummaryDto>>(response);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.Items.Count);

        var anaSummary = page.Items.Single(member => member.FirstName == "Ana");

        Assert.Equal("Bianchi", anaSummary.LastName);
        Assert.Equal("Stylist", anaSummary.JobTitle);
        Assert.True(anaSummary.IsActive);
        Assert.Equal(1, anaSummary.ServiceCount);

        var benSummary = page.Items.Single(member => member.FirstName == "Ben");

        Assert.False(benSummary.IsActive);
        Assert.Equal(0, benSummary.ServiceCount);
    }

    [Fact]
    public async Task Search_matches_the_term_and_narrows_by_status()
    {
        await Factory.SeedStaffAsync("Ana", "Bianchi", jobTitle: "Senior Stylist", email: "ana@debliss.ae");
        await Factory.SeedStaffAsync("Ben", "Osei", isActive: false);

        var byName = await ReadAsync<PagedResult<StaffSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=ana"));
        var byRole = await ReadAsync<PagedResult<StaffSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=STYLIST"));
        var byEmail = await ReadAsync<PagedResult<StaffSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=debliss.ae"));

        Assert.Equal("Ana", Assert.Single(byName.Items).FirstName);
        Assert.Equal("Ana", Assert.Single(byRole.Items).FirstName);
        Assert.Equal("Ana", Assert.Single(byEmail.Items).FirstName);

        var inactive = await ReadAsync<PagedResult<StaffSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?status=Inactive"));

        Assert.Equal("Ben", Assert.Single(inactive.Items).FirstName);

        var active = await ReadAsync<PagedResult<StaffSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?status=Active"));

        Assert.Equal("Ana", Assert.Single(active.Items).FirstName);
    }

    [Fact]
    public async Task Create_returns_201_with_a_location_that_reads_the_profile_back()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, SaveRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<StaffDto>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Jasmin", created.FirstName);
        Assert.Equal("Rae", created.LastName);
        Assert.Equal("jasmin@debliss.ae", created.Email);
        Assert.Equal("+971501234567", created.PhoneNumber);
        Assert.Equal("Stylist", created.JobTitle);
        Assert.Equal("#FF5733", created.CalendarColor);
        Assert.True(created.IsActive);
        Assert.Empty(created.Services);

        var readBack = await ReadAsync<StaffDto>(
            await Client.GetAsync(LocationPathOf(response)));

        Assert.Equal(created.Id, readBack.Id);
    }

    [Fact]
    public async Task Create_rejects_a_missing_name_with_a_field_error()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, SaveRequest(firstName: null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Contains("firstName", problem.Errors.Keys);
    }

    [Fact]
    public async Task GetById_returns_404_problem_for_an_unknown_member()
    {
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(404, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Update_replaces_the_profile()
    {
        var created = await ReadAsync<StaffDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{created.Id}",
            SaveRequest(firstName: "Jasmin", lastName: "Ray", jobTitle: "Senior Stylist", calendarColor: "#abc"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<StaffDto>(response);

        Assert.Equal("Ray", updated.LastName);
        Assert.Equal("Senior Stylist", updated.JobTitle);
        Assert.Equal("#abc", updated.CalendarColor);
    }

    [Fact]
    public async Task Patch_status_toggles_the_member_without_touching_the_profile()
    {
        var created = await ReadAsync<StaffDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<StaffDto>(response);

        Assert.False(updated.IsActive);
        Assert.Equal("Jasmin", updated.FirstName);
    }

    [Fact]
    public async Task Services_roundtrip_through_put_and_get()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var colour = await Factory.SeedServiceAsync(categoryId, "Colour", 80m);
        var cut = await Factory.SeedServiceAsync(categoryId, "Cut", 40m);
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var set = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{staffId}/services",
            new { serviceIds = new[] { colour, cut } });

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var assigned = await ReadAsync<IReadOnlyList<StaffServiceDto>>(set);

        Assert.Equal(
            new[] { "Colour", "Cut" },
            assigned.Select(service => service.Name).ToArray());
        Assert.All(assigned, service => Assert.Equal("Hair", service.CategoryName));

        var readBack = await ReadAsync<IReadOnlyList<StaffServiceDto>>(
            await Client.GetAsync($"{BaseUrl}/{staffId}/services"));

        Assert.Equal(2, readBack.Count);
    }

    [Fact]
    public async Task Services_reject_an_assignment_that_references_a_missing_service()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{staffId}/services",
            new { serviceIds = new[] { Guid.NewGuid() } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("serviceIds", problem.Errors.Keys);
    }

    [Fact]
    public async Task Working_hours_roundtrip_with_day_names_and_hhmm_times()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var set = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{staffId}/working-hours",
            WeekBody());

        Assert.Equal(HttpStatusCode.OK, set.StatusCode);

        var readBack = await ReadAsync<WeekScheduleDto>(
            await Client.GetAsync($"{BaseUrl}/{staffId}/working-hours"));

        Assert.Equal(7, readBack.Days.Count);
        Assert.Equal("Monday", readBack.Days[0].DayOfWeek);
        Assert.True(readBack.Days[0].IsWorkingDay);
        Assert.Equal(("09:00", "17:00"), (readBack.Days[0].Intervals[0].Start, readBack.Days[0].Intervals[0].End));
        Assert.Equal("Sunday", readBack.Days[6].DayOfWeek);
        Assert.False(readBack.Days[6].IsWorkingDay);
    }

    [Fact]
    public async Task Working_hours_reject_an_interval_ending_before_it_starts()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{staffId}/working-hours",
            WeekBody(start: "17:00", end: "09:00"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("days", problem.Errors.Keys);
    }

    [Fact]
    public async Task Time_off_creates_then_lists_with_the_type_name()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var request = new
        {
            type = "Leave",
            startsAt = MondayMorning,
            endsAt = MondayMorning.AddHours(8),
            reason = "Vacation"
        };

        var response = await Client.PostAsJsonAsync($"{BaseUrl}/{staffId}/time-off", request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<StaffTimeOffDto>(response);

        Assert.Equal("Leave", created.Type);
        Assert.Equal("Vacation", created.Reason);
        Assert.StartsWith($"/api/staff/{staffId}/time-off", LocationPathOf(response));

        var list = await ReadAsync<IReadOnlyList<StaffTimeOffDto>>(
            await Client.GetAsync($"{BaseUrl}/{staffId}/time-off"));

        var entry = Assert.Single(list);

        Assert.Equal(created.Id, entry.Id);
        Assert.Equal("Leave", entry.Type);
    }

    [Fact]
    public async Task Time_off_rejects_a_window_that_does_not_end_after_the_start()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{staffId}/time-off",
            new
            {
                type = "Sick",
                startsAt = MondayMorning,
                endsAt = MondayMorning,
                reason = (string?)null
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("endsAt", problem.Errors.Keys);
    }

    [Fact]
    public async Task Time_off_can_be_removed_while_it_is_still_in_the_future()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var created = await ReadAsync<StaffTimeOffDto>(
            await Client.PostAsJsonAsync(
                $"{BaseUrl}/{staffId}/time-off",
                new
                {
                    type = "Leave",
                    startsAt = MondayMorning.AddDays(1),
                    endsAt = MondayMorning.AddDays(1).AddHours(8),
                    reason = (string?)null
                }));

        var response = await Client.DeleteAsync($"{BaseUrl}/{staffId}/time-off/{created.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var list = await ReadAsync<IReadOnlyList<StaffTimeOffDto>>(
            await Client.GetAsync($"{BaseUrl}/{staffId}/time-off"));

        Assert.Empty(list);
    }

    [Fact]
    public async Task Time_off_that_has_started_cannot_be_deleted()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");
        var started = new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);
        var created = await ReadAsync<StaffTimeOffDto>(
            await Client.PostAsJsonAsync(
                $"{BaseUrl}/{staffId}/time-off",
                new
                {
                    type = "Sick",
                    startsAt = started,
                    endsAt = started.AddHours(8),
                    reason = (string?)null
                }));

        var response = await Client.DeleteAsync($"{BaseUrl}/{staffId}/time-off/{created.Id}");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(409, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Availability_answers_over_http_and_honours_time_off()
    {
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        await Client.PutAsJsonAsync($"{BaseUrl}/{staffId}/working-hours", WeekBody());

        var start = MondayMorning.AddHours(1);
        var end = start.AddHours(1);

        var url = $"{BaseUrl}/availability?staffId={staffId}" +
            $"&start={Uri.EscapeDataString(start.ToString("O"))}" +
            $"&end={Uri.EscapeDataString(end.ToString("O"))}";

        var free = await ReadAsync<StaffAvailabilityCheckDto>(await Client.GetAsync(url));

        Assert.True(free.IsAvailable);
        Assert.Equal(staffId, free.StaffId);

        await Client.PostAsJsonAsync(
            $"{BaseUrl}/{staffId}/time-off",
            new
            {
                type = "Leave",
                startsAt = start,
                endsAt = end,
                reason = (string?)null
            });

        var blocked = await ReadAsync<StaffAvailabilityCheckDto>(await Client.GetAsync(url));

        Assert.False(blocked.IsAvailable);
    }
}
