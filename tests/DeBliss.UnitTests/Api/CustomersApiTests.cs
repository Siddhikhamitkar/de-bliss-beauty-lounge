using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Drives the real HTTP pipeline for the customer list, profile, status,
/// notes and appointment history. The rules are covered by
/// <see cref="Customers.CustomerServiceTests"/>; these tests catch what the
/// wire adds: query binding, the RFC 9457 problem bodies, and the JSON shape
/// the React app consumes.
/// </summary>
public sealed class CustomersApiTests : ApiTestBase
{
    private const string BaseUrl = "/api/customers";

    private static readonly DateTimeOffset VisitAfternoon =
        new(2026, 9, 14, 14, 0, 0, TimeSpan.Zero);

    private static object SaveRequest(
        string? firstName = "Sara",
        string? lastName = "Haddad",
        string? phoneNumber = "+971 50 000 0000",
        string? email = "sara@debliss.ae",
        string? dateOfBirth = "1990-04-12",
        string? gender = "Female",
        string? address = "Jumeirah",
        bool? isActive = true,
        bool allowDuplicatePhone = false) =>
        new
        {
            firstName,
            lastName,
            phoneNumber,
            email,
            dateOfBirth,
            gender,
            address,
            isActive,
            allowDuplicatePhone
        };

    [Fact]
    public async Task Search_returns_paged_summaries()
    {
        await Factory.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0001");
        await Factory.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 000 0002", isActive: false);

        var response = await Client.GetAsync(BaseUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await ReadAsync<PagedResult<CustomerSummaryDto>>(response);

        Assert.Equal(2, page.TotalCount);
        Assert.Equal(1, page.Page);
        Assert.Equal(2, page.Items.Count);

        var sara = page.Items.Single(customer => customer.FirstName == "Sara");

        Assert.Equal("Haddad", sara.LastName);
        Assert.Equal("+971 50 000 0001", sara.PhoneNumber);
        Assert.True(sara.IsActive);

        Assert.False(page.Items.Single(customer => customer.FirstName == "Ben").IsActive);
    }

    [Fact]
    public async Task Search_matches_name_email_phone_digits_and_narrows_by_status()
    {
        await Factory.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 123 4567", email: "sara@debliss.ae");
        await Factory.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 999 9999", isActive: false);

        var byName = await ReadAsync<PagedResult<CustomerSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=haddad"));
        var byEmail = await ReadAsync<PagedResult<CustomerSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=DEBLISS.AE"));
        var byPhoneDigits = await ReadAsync<PagedResult<CustomerSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?search=501234567"));

        Assert.Equal("Sara", Assert.Single(byName.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byEmail.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byPhoneDigits.Items).FirstName);

        var inactive = await ReadAsync<PagedResult<CustomerSummaryDto>>(
            await Client.GetAsync($"{BaseUrl}?status=Inactive"));

        Assert.Equal("Ben", Assert.Single(inactive.Items).FirstName);
    }

    [Fact]
    public async Task Create_returns_201_with_a_location_that_reads_the_profile_back()
    {
        var response = await Client.PostAsJsonAsync(BaseUrl, SaveRequest());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<CustomerDto>(response);

        Assert.NotEqual(Guid.Empty, created.Id);
        Assert.Equal("Sara", created.FirstName);
        Assert.Equal("Haddad", created.LastName);
        Assert.Equal("+971 50 000 0000", created.PhoneNumber);
        Assert.Equal("sara@debliss.ae", created.Email);
        Assert.Equal(new DateOnly(1990, 4, 12), created.DateOfBirth);
        Assert.Equal("Female", created.Gender);
        Assert.Equal("Jumeirah", created.Address);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.AppointmentSummary.TotalAppointments);
        Assert.Equal(0, created.AppointmentSummary.NoShowAppointments);
        Assert.Null(created.AppointmentSummary.LastVisitAt);

        var readBack = await ReadAsync<CustomerDto>(
            await Client.GetAsync(LocationPathOf(response)));

        Assert.Equal(created.Id, readBack.Id);
    }

    [Fact]
    public async Task Create_rejects_invalid_fields_with_field_errors()
    {
        var response = await Client.PostAsJsonAsync(
            BaseUrl,
            SaveRequest(firstName: null, phoneNumber: "abc", email: "not-an-email"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Equal(400, problem.Status);
        Assert.Contains("firstName", problem.Errors.Keys);
        Assert.Contains("phoneNumber", problem.Errors.Keys);
        Assert.Contains("email", problem.Errors.Keys);
    }

    [Fact]
    public async Task Create_warns_on_a_duplicate_phone_then_allows_it_once_confirmed()
    {
        await Factory.SeedCustomerAsync("Lina", "Mansour", phoneNumber: "+971 50 000 0000");

        var conflict = await Client.PostAsJsonAsync(
            BaseUrl,
            SaveRequest(firstName: "Lina", lastName: "Mansour", phoneNumber: "971500000000"));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        var problem = await ReadAsync<Problem>(conflict);

        Assert.Equal(409, problem.Status);
        Assert.Contains("Lina Mansour", problem.Detail);

        var confirmed = await Client.PostAsJsonAsync(
            BaseUrl,
            SaveRequest(firstName: "Lina", lastName: "Mansour", phoneNumber: "971500000000", allowDuplicatePhone: true));

        Assert.Equal(HttpStatusCode.Created, confirmed.StatusCode);
    }

    [Fact]
    public async Task GetById_returns_404_problem_for_an_unknown_customer()
    {
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal(404, problem.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Detail));
    }

    [Fact]
    public async Task Update_replaces_the_profile_and_keeps_the_id()
    {
        var created = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{created.Id}",
            SaveRequest(firstName: "Sarah", lastName: "Haddad", phoneNumber: "+971 50 111 1111", email: "sarah@debliss.ae"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<CustomerDto>(response);

        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("Sarah", updated.FirstName);
        Assert.Equal("+971 50 111 1111", updated.PhoneNumber);
        Assert.Equal("sarah@debliss.ae", updated.Email);
    }

    [Fact]
    public async Task Patch_status_toggles_the_customer_without_touching_the_profile()
    {
        var created = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.PatchAsJsonAsync(
            $"{BaseUrl}/{created.Id}/status",
            new { isActive = false });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var updated = await ReadAsync<CustomerDto>(response);

        Assert.False(updated.IsActive);
        Assert.Equal("Sara", updated.FirstName);
    }

    [Fact]
    public async Task Notes_roundtrip_through_post_get_and_put()
    {
        var customer = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));
        await Factory.SeedCustomerNoteAsync(customer.Id, "Prefers Saturday mornings.");

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{customer.Id}/notes",
            new { note = "Allergic to lavender oil." });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<CustomerNoteDto>(response);

        Assert.Equal("Allergic to lavender oil.", created.Note);
        Assert.Equal(customer.Id, created.CustomerId);

        var list = await ReadAsync<IReadOnlyList<CustomerNoteDto>>(
            await Client.GetAsync($"{BaseUrl}/{customer.Id}/notes"));

        Assert.Equal(
            new[] { "Allergic to lavender oil.", "Prefers Saturday mornings." },
            list.Select(note => note.Note).ToArray());

        var edited = await Client.PutAsJsonAsync(
            $"{BaseUrl}/{customer.Id}/notes/{list[1].Id}",
            new { note = "Prefers Sunday mornings." });

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal("Prefers Sunday mornings.", (await ReadAsync<CustomerNoteDto>(edited)).Note);
    }

    [Fact]
    public async Task Notes_reject_empty_content_with_a_field_error()
    {
        var customer = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.PostAsJsonAsync(
            $"{BaseUrl}/{customer.Id}/notes",
            new { note = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains("note", problem.Errors.Keys);
    }

    [Fact]
    public async Task Appointments_reads_the_scheduling_tables_with_snapshots()
    {
        var customer = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Cut", 40m);
        var staffId = await Factory.SeedStaffAsync("Ana", "Bianchi");

        await Factory.SeedCustomerAppointmentAsync(
            customer.Id,
            VisitAfternoon,
            lines: [(serviceId, staffId, "Haircut")]);

        var response = await Client.GetAsync($"{BaseUrl}/{customer.Id}/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var appointments = await ReadAsync<IReadOnlyList<CustomerAppointmentDto>>(response);
        var visit = Assert.Single(appointments);

        Assert.Equal("Completed", visit.Status);
        Assert.Equal(["Haircut"], visit.ServiceNames);
        Assert.Equal(["Ana Bianchi"], visit.StaffNames);
        Assert.Equal(VisitAfternoon, visit.StartsAt);

        var profile = await ReadAsync<CustomerDto>(
            await Client.GetAsync($"{BaseUrl}/{customer.Id}"));

        Assert.Equal(1, profile.AppointmentSummary.TotalAppointments);
        Assert.Equal(1, profile.AppointmentSummary.CompletedAppointments);
        Assert.Equal(VisitAfternoon, profile.AppointmentSummary.LastVisitAt);
    }

    [Fact]
    public async Task Appointments_is_empty_until_the_appointment_module_writes_bookings()
    {
        var customer = await ReadAsync<CustomerDto>(
            await Client.PostAsJsonAsync(BaseUrl, SaveRequest()));

        var response = await Client.GetAsync($"{BaseUrl}/{customer.Id}/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await ReadAsync<IReadOnlyList<CustomerAppointmentDto>>(response));
    }

    [Fact]
    public async Task Appointments_returns_404_for_an_unknown_customer()
    {
        var response = await Client.GetAsync($"{BaseUrl}/{Guid.NewGuid()}/appointments");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
