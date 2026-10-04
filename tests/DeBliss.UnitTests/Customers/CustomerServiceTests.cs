using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Customers.Dtos;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Customers;

public class CustomerServiceTests
{
    private static readonly DateTimeOffset VisitAfternoon =
        new(2026, 9, 14, 14, 0, 0, TimeSpan.Zero);

    private static SaveCustomerRequest Request(
        string? firstName = "Sara",
        string? lastName = "Haddad",
        string? phoneNumber = "+971 50 000 0000",
        string? email = null,
        DateOnly? dateOfBirth = null,
        string? gender = null,
        string? address = null,
        bool? isActive = true,
        bool allowDuplicatePhone = false) =>
        new(firstName, lastName, phoneNumber, email, dateOfBirth, gender, address, isActive, allowDuplicatePhone);

    private static CustomerQuery Query(
        string? search = null,
        CustomerStatusFilter status = CustomerStatusFilter.All,
        int page = 1,
        int pageSize = 50) =>
        new(search, status, page, pageSize);

    [Fact]
    public async Task Create_saves_the_profile_and_defaults_to_active()
    {
        using var harness = new CustomersHarness();

        var created = await harness.Customers.CreateAsync(Request(
            email: " sara@example.ae ",
            dateOfBirth: new DateOnly(1990, 4, 12),
            gender: "Female",
            address: "Jumeirah",
            isActive: null));

        Assert.Equal("Sara", created.FirstName);
        Assert.Equal("Haddad", created.LastName);
        Assert.Equal("+971 50 000 0000", created.PhoneNumber);
        Assert.Equal("sara@example.ae", created.Email);
        Assert.Equal(new DateOnly(1990, 4, 12), created.DateOfBirth);
        Assert.Equal("Female", created.Gender);
        Assert.Equal("Jumeirah", created.Address);
        Assert.True(created.IsActive);
        Assert.Equal(0, created.AppointmentSummary.TotalAppointments);
        Assert.Equal(0, created.AppointmentSummary.CompletedAppointments);
        Assert.Equal(0, created.AppointmentSummary.CancelledAppointments);
        Assert.Equal(0, created.AppointmentSummary.NoShowAppointments);
        Assert.Null(created.AppointmentSummary.LastVisitAt);
        Assert.Equal(harness.Database.Clock.Now, created.CreatedAt);

        await using var verify = harness.Reopen();

        var stored = await verify.Customers.SingleAsync();

        Assert.True(stored.IsActive);
    }

    [Theory]
    [InlineData(null, "Haddad", "+971 50 000 0000", "firstName")]
    [InlineData("", "Haddad", "+971 50 000 0000", "firstName")]
    [InlineData("Sara", null, "+971 50 000 0000", "lastName")]
    [InlineData("Sara", "Haddad", null, "phoneNumber")]
    [InlineData("Sara", "Haddad", "   ", "phoneNumber")]
    [InlineData("Sara", "Haddad", "abc", "phoneNumber")]
    [InlineData("Sara", "Haddad", "1234", "phoneNumber")]
    public async Task Create_rejects_invalid_required_fields(
        string? firstName,
        string? lastName,
        string? phoneNumber,
        string expectedKey)
    {
        using var harness = new CustomersHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Customers.CreateAsync(Request(firstName, lastName, phoneNumber)));

        Assert.Single(failure.Errors);
        Assert.Contains(expectedKey, failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_rejects_every_invalid_field_in_one_report()
    {
        using var harness = new CustomersHarness();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Customers.CreateAsync(Request(
                firstName: null,
                phoneNumber: "not-a-number",
                email: "not-an-email",
                dateOfBirth: DateOnly.FromDateTime(DateTime.UtcNow.AddDays(1)))));

        Assert.Contains("firstName", failure.Errors.Keys);
        Assert.Contains("phoneNumber", failure.Errors.Keys);
        Assert.Contains("email", failure.Errors.Keys);
        Assert.Contains("dateOfBirth", failure.Errors.Keys);
    }

    [Fact]
    public async Task Create_stores_blank_optional_fields_as_null()
    {
        using var harness = new CustomersHarness();

        var created = await harness.Customers.CreateAsync(Request(email: "  ", gender: " ", address: ""));

        Assert.Null(created.Email);
        Assert.Null(created.Gender);
        Assert.Null(created.Address);
    }

    [Fact]
    public async Task Create_accepts_any_reasonable_phone_formatting()
    {
        using var harness = new CustomersHarness();

        var created = await harness.Customers.CreateAsync(Request(phoneNumber: "050-000 0000"));

        Assert.Equal("050-000 0000", created.PhoneNumber);
    }

    [Fact]
    public async Task Create_rejects_a_phone_number_another_customer_already_uses()
    {
        using var harness = new CustomersHarness();
        await harness.SeedCustomerAsync("Lina", "Mansour", phoneNumber: "+971 50 000 0000");

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Customers.CreateAsync(Request(phoneNumber: "971500000000")));

        Assert.Contains("Lina Mansour", failure.Message);
    }

    [Fact]
    public async Task Create_allows_the_duplicate_once_the_caller_confirms_it()
    {
        using var harness = new CustomersHarness();
        await harness.SeedCustomerAsync("Lina", "Mansour", phoneNumber: "+971 50 000 0000");

        var created = await harness.Customers.CreateAsync(Request(
            firstName: "Lina",
            lastName: "Mansour",
            phoneNumber: "971 5000 00000",
            allowDuplicatePhone: true));

        Assert.Equal("Mansour", created.LastName);

        await using var verify = harness.Reopen();

        Assert.Equal(2, await verify.Customers.CountAsync());
    }

    [Fact]
    public async Task Update_persists_the_new_details_and_keeps_the_id()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        var updated = await harness.Customers.UpdateAsync(id, Request(
            firstName: "Sarah",
            lastName: "Haddad",
            phoneNumber: "+971 50 111 1111",
            email: "sarah@example.ae"));

        Assert.Equal(id, updated.Id);
        Assert.Equal("Sarah", updated.FirstName);
        Assert.Equal("+971 50 111 1111", updated.PhoneNumber);
        Assert.Equal("sarah@example.ae", updated.Email);

        await using var verify = harness.Reopen();

        var stored = await verify.Customers.SingleAsync();

        Assert.Equal("Sarah", stored.FirstName);
    }

    [Fact]
    public async Task Update_leaves_status_alone_when_the_request_omits_it()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000", isActive: false);

        var updated = await harness.Customers.UpdateAsync(id, Request(isActive: null));

        Assert.False(updated.IsActive);
    }

    [Fact]
    public async Task Update_warns_when_the_new_phone_belongs_to_someone_else()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");
        await harness.SeedCustomerAsync("Lina", "Mansour", phoneNumber: "+971 50 999 9999");

        var failure = await Assert.ThrowsAsync<ConflictException>(
            () => harness.Customers.UpdateAsync(id, Request(phoneNumber: "971509999999")));

        Assert.Contains("Lina Mansour", failure.Message);
    }

    [Fact]
    public async Task Update_does_not_flag_the_customers_own_number_as_a_duplicate()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        var updated = await harness.Customers.UpdateAsync(id, Request(phoneNumber: "971500000000"));

        Assert.Equal(id, updated.Id);
    }

    [Fact]
    public async Task SetStatus_toggles_the_customer_without_touching_the_profile()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        var updated = await harness.Customers.SetStatusAsync(id, false);

        Assert.False(updated.IsActive);
        Assert.Equal("Sara", updated.FirstName);
    }

    [Fact]
    public async Task Get_throws_for_an_unknown_customer()
    {
        using var harness = new CustomersHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Customers.GetAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task Search_matches_first_last_full_name_email_and_phone_digits()
    {
        using var harness = new CustomersHarness();
        await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 123 4567", email: "sara@example.ae");
        await harness.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 999 9999");

        var byFirstName = await harness.Customers.SearchAsync(Query(search: "sara"));
        var byLastName = await harness.Customers.SearchAsync(Query(search: "haddad"));
        var byFullName = await harness.Customers.SearchAsync(Query(search: "ara hadd"));
        var byEmail = await harness.Customers.SearchAsync(Query(search: "EXAMPLE.AE"));
        var byPhoneDigits = await harness.Customers.SearchAsync(Query(search: "501234567"));

        Assert.Equal("Sara", Assert.Single(byFirstName.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byLastName.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byFullName.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byEmail.Items).FirstName);
        Assert.Equal("Sara", Assert.Single(byPhoneDigits.Items).FirstName);
    }

    [Fact]
    public async Task Search_narrows_by_status()
    {
        using var harness = new CustomersHarness();
        await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0001");
        await harness.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 000 0002", isActive: false);

        var active = await harness.Customers.SearchAsync(Query(status: CustomerStatusFilter.Active));
        var inactive = await harness.Customers.SearchAsync(Query(status: CustomerStatusFilter.Inactive));

        Assert.Equal("Sara", Assert.Single(active.Items).FirstName);
        Assert.False(Assert.Single(inactive.Items).IsActive);
    }

    [Fact]
    public async Task Search_pages_by_last_name_with_a_total_count()
    {
        using var harness = new CustomersHarness();
        await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0001");
        await harness.SeedCustomerAsync("Ben", "Osei", phoneNumber: "+971 50 000 0002");
        await harness.SeedCustomerAsync("Ana", "Bianchi", phoneNumber: "+971 50 000 0003");

        var page = await harness.Customers.SearchAsync(Query(page: 2, pageSize: 2));

        Assert.Equal(3, page.TotalCount);
        Assert.Equal(2, page.Page);
        Assert.Equal(2, page.PageSize);
        Assert.Single(page.Items);
        Assert.Equal("Ben", page.Items[0].FirstName);
    }

    [Fact]
    public async Task Notes_roundtrip_newest_first_and_edit_in_place()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");
        await harness.SeedNoteAsync(id, "Prefers Saturday mornings.");
        harness.Database.Clock.Advance(TimeSpan.FromMinutes(1));

        var added = await harness.Customers.AddNoteAsync(id, new SaveCustomerNoteRequest("Allergic to lavender oil."));

        var notes = await harness.Customers.ListNotesAsync(id);

        Assert.Equal(2, notes.Count);
        Assert.Equal("Allergic to lavender oil.", notes[0].Note);
        Assert.Equal("Prefers Saturday mornings.", notes[1].Note);
        Assert.Equal(added.Id, notes[0].Id);
        Assert.Equal(id, notes[0].CustomerId);

        var edited = await harness.Customers.UpdateNoteAsync(id, notes[1].Id, new SaveCustomerNoteRequest("Prefers Sunday mornings."));

        Assert.Equal("Prefers Sunday mornings.", edited.Note);
        Assert.Equal(notes[1].Id, edited.Id);

        var after = await harness.Customers.ListNotesAsync(id);

        Assert.Equal(2, after.Count);
    }

    [Fact]
    public async Task Notes_reject_empty_and_over_long_content()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        var empty = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Customers.AddNoteAsync(id, new SaveCustomerNoteRequest("   ")));
        var overLong = await Assert.ThrowsAsync<ValidationFailedException>(
            () => harness.Customers.AddNoteAsync(id, new SaveCustomerNoteRequest(new string('x', 2001))));

        Assert.Contains("note", empty.Errors.Keys);
        Assert.Contains("note", overLong.Errors.Keys);
    }

    [Fact]
    public async Task Notes_throw_for_an_unknown_customer_and_unknown_note()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Customers.ListNotesAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Customers.AddNoteAsync(Guid.NewGuid(), new SaveCustomerNoteRequest("Hello")));
        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Customers.UpdateNoteAsync(id, Guid.NewGuid(), new SaveCustomerNoteRequest("Hello")));
    }

    [Fact]
    public async Task Appointment_history_reads_the_scheduling_tables_newest_first()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var serviceId = await harness.SeedServiceAsync(categoryId, "Cut");
        var staffId = await harness.SeedStaffAsync("Ana", "Bianchi");

        await harness.SeedAppointmentAsync(
            id,
            VisitAfternoon,
            AppointmentStatus.Completed,
            [(serviceId, staffId, "Haircut")]);
        await harness.SeedAppointmentAsync(
            id,
            VisitAfternoon.AddDays(7),
            AppointmentStatus.Cancelled,
            [(serviceId, null, "Colour")]);
        await harness.SeedAppointmentAsync(
            id,
            VisitAfternoon.AddDays(-7),
            AppointmentStatus.NoShow);

        var appointments = await harness.Customers.ListAppointmentsAsync(id);

        Assert.Equal(3, appointments.Count);
        Assert.Equal("Cancelled", appointments[0].Status);
        Assert.Equal(["Colour"], appointments[0].ServiceNames);
        Assert.Empty(appointments[0].StaffNames);
        Assert.Equal("Completed", appointments[1].Status);
        Assert.Equal(["Haircut"], appointments[1].ServiceNames);
        Assert.Equal(["Ana Bianchi"], appointments[1].StaffNames);
        Assert.Equal("NoShow", appointments[2].Status);

        var profile = await harness.Customers.GetAsync(id);

        Assert.Equal(3, profile.AppointmentSummary.TotalAppointments);
        Assert.Equal(1, profile.AppointmentSummary.CompletedAppointments);
        Assert.Equal(1, profile.AppointmentSummary.CancelledAppointments);
        Assert.Equal(1, profile.AppointmentSummary.NoShowAppointments);
        Assert.Equal(VisitAfternoon, profile.AppointmentSummary.LastVisitAt);
    }

    [Fact]
    public async Task Appointment_history_ignores_soft_deleted_appointments()
    {
        using var harness = new CustomersHarness();
        var id = await harness.SeedCustomerAsync("Sara", "Haddad", phoneNumber: "+971 50 000 0000");

        await harness.SeedAppointmentAsync(id, VisitAfternoon, AppointmentStatus.Completed);

        await using var context = harness.Reopen();

        var appointment = await context.Appointments.SingleAsync();
        appointment.DeletedAt = harness.Database.Clock.Now;
        await context.SaveChangesAsync();

        var appointments = await harness.Customers.ListAppointmentsAsync(id);
        var profile = await harness.Customers.GetAsync(id);

        Assert.Empty(appointments);
        Assert.Equal(0, profile.AppointmentSummary.TotalAppointments);
        Assert.Equal(0, profile.AppointmentSummary.NoShowAppointments);
        Assert.Null(profile.AppointmentSummary.LastVisitAt);
    }

    [Fact]
    public async Task Appointment_history_throws_for_an_unknown_customer()
    {
        using var harness = new CustomersHarness();

        await Assert.ThrowsAsync<NotFoundException>(
            () => harness.Customers.ListAppointmentsAsync(Guid.NewGuid()));
    }
}
