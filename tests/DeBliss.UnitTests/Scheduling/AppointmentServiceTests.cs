using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Scheduling;

/// <summary>
/// The booking rules: snapshot capture onto the lines, the availability gate,
/// the drag/resize schedule patch and the status pipeline with its history
/// rows. The wire-level coverage lives in
/// <see cref="Api.AppointmentsApiTests"/>.
/// </summary>
public class AppointmentServiceTests
{
    private static readonly DateTimeOffset Monday =
        new(2026, 10, 5, 0, 0, 0, TimeSpan.Zero);

    private static DateTimeOffset At(int hour, int minute = 0) =>
        Monday.AddHours(hour).AddMinutes(minute);

    private sealed record Fixture(
        SchedulingHarness Harness,
        Guid CustomerId,
        Guid StaffId,
        Guid ServiceId,
        Guid SecondServiceId,
        Guid CategoryId);

    private static async Task<Fixture> ArrangeAsync()
    {
        var harness = new SchedulingHarness();
        var customerId = await harness.SeedCustomerAsync("Sara", "Haddad", "+971 50 000 0001");
        var categoryId = await harness.SeedCategoryAsync("Hair");
        var staffId = await harness.SeedStaffAsync("Jasmin", "Rae");
        await harness.SeedWorkingHoursAsync(staffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        var serviceId = await harness.SeedServiceAsync(categoryId, "Haircut", durationMinutes: 30, price: 100m, staffIds: [staffId]);
        var secondServiceId = await harness.SeedServiceAsync(categoryId, "Colour", durationMinutes: 60, price: 150m, staffIds: [staffId]);

        return new Fixture(harness, customerId, staffId, serviceId, secondServiceId, categoryId);
    }

    private static SaveAppointmentRequest Request(
        Guid? customerId,
        DateTimeOffset? startsAt = null,
        IReadOnlyList<AppointmentServiceLineInput>? services = null,
        string? walkInCustomerName = null,
        string? walkInCustomerPhone = null,
        string? notes = null,
        Guid? serviceBundleId = null) =>
        new(customerId, walkInCustomerName, walkInCustomerPhone, startsAt ?? At(10), services ?? [], notes, serviceBundleId);

    private static async Task<Appointment> ReloadAsync(SchedulingHarness harness, Guid appointmentId)
    {
        await using var context = harness.Reopen();

        return await context.Appointments
            .Include(appointment => appointment.StatusHistory)
            .Include(appointment => appointment.Services)
            .SingleAsync(appointment => appointment.Id == appointmentId);
    }

    [Fact]
    public async Task Creating_books_every_line_with_its_snapshot_and_a_confirmed_status()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services:
            [
                new(fixture.ServiceId, fixture.StaffId),
                new(fixture.SecondServiceId, fixture.StaffId)
            ]));

        Assert.Equal(fixture.CustomerId, created.CustomerId);
        Assert.Equal("Sara Haddad", created.CustomerName);
        Assert.Equal("+971 50 000 0001", created.CustomerPhone);
        Assert.Equal(At(10), created.StartsAt);
        Assert.Equal(At(11, 30), created.EndsAt);
        Assert.Equal("Confirmed", created.Status);
        Assert.Equal("Reception", created.Source);
        Assert.Equal(250m, created.TotalPrice);
        Assert.Equal(90, created.TotalDurationMinutes);

        Assert.Equal(2, created.Services.Count);
        Assert.Equal("Haircut", created.Services[0].ServiceName);
        Assert.Equal(30, created.Services[0].DurationMinutes);
        Assert.Equal(100m, created.Services[0].UnitPrice);
        Assert.Equal("Jasmin", created.Services[0].StaffFirstName);
        Assert.Equal("Colour", created.Services[1].ServiceName);

        var saved = await ReloadAsync(fixture.Harness, created.Id);
        var history = saved.StatusHistory.OrderBy(row => row.ChangedAt).ToList();

        var initial = Assert.Single(history);
        Assert.Null(initial.FromStatus);
        Assert.Equal(AppointmentStatus.Confirmed, initial.ToStatus);
    }

    [Fact]
    public async Task Creating_for_a_walk_in_records_the_name_and_phone_without_a_customer()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(null, services: [new(fixture.ServiceId, fixture.StaffId)],
                walkInCustomerName: "Maya", walkInCustomerPhone: "+971 50 111 2222"));

        Assert.Null(created.CustomerId);
        Assert.Equal("Maya", created.CustomerName);
        Assert.Equal("+971 50 111 2222", created.CustomerPhone);
    }

    [Fact]
    public async Task A_customer_and_a_walk_in_name_together_are_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, walkInCustomerName: "Maya")));

        Assert.True(failure.Errors.ContainsKey("customerId"));
        Assert.Contains(failure.Errors["customerId"], message => message.Contains("not both", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Neither_a_customer_nor_a_walk_in_name_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(Request(null)));

        Assert.True(failure.Errors.ContainsKey("customerId"));
    }

    [Fact]
    public async Task A_start_in_the_past_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var past = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, startsAt: past, services: [new(fixture.ServiceId, fixture.StaffId)])));

        Assert.Contains(failure.Errors["startsAt"], message => message.Contains("in the past", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_booking_without_services_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(Request(fixture.CustomerId)));

        Assert.Contains(failure.Errors["services"], message => message.Contains("At least one service", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_customer_is_a_404()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(Guid.NewGuid(), services: [new(fixture.ServiceId, fixture.StaffId)])));
    }

    [Fact]
    public async Task An_unknown_service_is_a_404()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new AppointmentServiceLineInput(Guid.NewGuid(), fixture.StaffId)])));
    }

    [Fact]
    public async Task An_unknown_staff_member_is_a_404()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new AppointmentServiceLineInput(fixture.ServiceId, Guid.NewGuid())])));
    }

    [Fact]
    public async Task An_inactive_service_is_no_longer_bookable()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var retired = await fixture.Harness.SeedServiceAsync(
            await fixture.Harness.SeedCategoryAsync("Nails"), "Old Manicure", isActive: false, staffIds: [fixture.StaffId]);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new AppointmentServiceLineInput(retired, fixture.StaffId)])));

        Assert.Contains(failure.Errors["services"], message => message.Contains("no longer bookable", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_staff_member_not_assigned_to_the_service_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var unassigned = await fixture.Harness.SeedStaffAsync("Omar", "Faris");
        await fixture.Harness.SeedWorkingHoursAsync(unassigned, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new AppointmentServiceLineInput(fixture.ServiceId, unassigned)])));

        Assert.Contains(
            failure.Errors["services"],
            message => message.Contains("is not assigned to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Time_off_overlapping_the_window_is_a_409()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        await fixture.Harness.SeedTimeOffAsync(fixture.StaffId, At(10), At(11), reason: "Trip");

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)])));

        Assert.Contains("Jasmin Rae is not available at the selected time.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_overlapping_confirmed_booking_on_the_same_member_is_a_409()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11),
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)])));

        Assert.Contains("is not available at the selected time.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_overlapping_booking_on_a_different_member_does_not_conflict()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var otherStaffId = await fixture.Harness.SeedStaffAsync("Nadia", "Saleh");
        await fixture.Harness.SeedWorkingHoursAsync(otherStaffId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        var otherServiceId = await fixture.Harness.SeedServiceAsync(
            await fixture.Harness.SeedCategoryAsync("Skin"), "Facial", durationMinutes: 60, price: 200m, staffIds: [otherStaffId]);
        await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11),
            lines: [new SchedulingSeed.Line(otherServiceId, otherStaffId, "Facial")]);

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        Assert.Equal(At(10), created.StartsAt);
    }

    [Fact]
    public async Task A_cancelled_booking_frees_its_slot_again()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11), AppointmentStatus.Cancelled,
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, At(10, 30), [new(fixture.ServiceId, fixture.StaffId)]));

        Assert.Equal(At(10, 30), created.StartsAt);
    }

    [Fact]
    public async Task A_full_update_replaces_the_lines_and_recomputes_the_end()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services:
            [
                new(fixture.ServiceId, fixture.StaffId),
                new(fixture.SecondServiceId, fixture.StaffId)
            ]));

        var updated = await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, At(13), [new(fixture.SecondServiceId, fixture.StaffId)]));

        Assert.Equal(At(13), updated.StartsAt);
        Assert.Equal(At(14), updated.EndsAt);

        var line = Assert.Single(updated.Services);
        Assert.Equal("Colour", line.ServiceName);
        Assert.Equal(150m, updated.TotalPrice);
    }

    /// <summary>
    /// The bundle over the two fixture services, listed Colour first so a
    /// booking submitted the other way round has to be put back in sequence.
    /// The components total 250, so 220 leaves a 30 saving.
    /// </summary>
    private static async Task<Guid> SeedMakeoverBundleAsync(Fixture fixture) =>
        await fixture.Harness.SeedBundleAsync(
            fixture.CategoryId, "Full Makeover", 220m, [fixture.SecondServiceId, fixture.ServiceId]);

    private static SaveAppointmentRequest BundleRequest(Fixture fixture, Guid bundleId, DateTimeOffset? startsAt = null) =>
        Request(fixture.CustomerId, startsAt,
        [
            new(fixture.ServiceId, fixture.StaffId),
            new(fixture.SecondServiceId, fixture.StaffId)
        ], serviceBundleId: bundleId);

    [Fact]
    public async Task A_bundle_booking_keeps_every_component_in_the_bundle_sequence()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);

        var created = await fixture.Harness.Appointments.CreateAsync(BundleRequest(fixture, bundleId));

        Assert.Equal(bundleId, created.ServiceBundleId);
        Assert.Equal("Full Makeover", created.BundleName);
        Assert.Equal(220m, created.BundlePrice);
        Assert.Equal(At(10), created.StartsAt);
        Assert.Equal(At(11, 30), created.EndsAt);
        Assert.Equal(90, created.TotalDurationMinutes);

        // Each line keeps its own price, so the lines still sum to the standard
        // total the bundle saving is measured against.
        Assert.Equal(250m, created.TotalPrice);
        Assert.Equal("Colour", created.Services[0].ServiceName);
        Assert.Equal("Haircut", created.Services[1].ServiceName);

        var saved = await ReloadAsync(fixture.Harness, created.Id);

        Assert.Equal(bundleId, saved.ServiceBundleId);
        Assert.Equal(220m, saved.BundlePriceSnapshot);
        Assert.Equal(
            new[] { "Colour", "Haircut" },
            saved.Services.OrderBy(line => line.SortOrder).Select(line => line.ServiceNameSnapshot).ToArray());
    }

    [Fact]
    public async Task A_bundle_booking_must_submit_every_component()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId,
                    services: [new(fixture.ServiceId, fixture.StaffId)],
                    serviceBundleId: bundleId)));

        Assert.Equal(
            "Bundle 'Full Makeover' contains: Colour, Haircut. Submit exactly those services.",
            Assert.Single(failure.Errors["services"]));
    }

    [Fact]
    public async Task A_bundle_booking_may_not_swap_a_component_for_another_service()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);
        var facialId = await fixture.Harness.SeedServiceAsync(
            await fixture.Harness.SeedCategoryAsync("Skin"),
            "Facial",
            durationMinutes: 60,
            price: 150m,
            staffIds: [fixture.StaffId]);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId,
                    services:
                    [
                        new(fixture.ServiceId, fixture.StaffId),
                        new(facialId, fixture.StaffId)
                    ],
                    serviceBundleId: bundleId)));

        Assert.Contains(
            failure.Errors["services"],
            message => message.Contains("Submit exactly those services.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_bundle_component_may_only_be_booked_once()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId,
                    services:
                    [
                        new(fixture.ServiceId, fixture.StaffId),
                        new(fixture.ServiceId, fixture.StaffId)
                    ],
                    serviceBundleId: bundleId)));

        Assert.Equal(
            "Each service of a bundle may only be booked once.",
            Assert.Single(failure.Errors["services"]));
    }

    [Fact]
    public async Task Booking_an_unknown_bundle_is_a_404()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await Assert.ThrowsAsync<NotFoundException>(() =>
            fixture.Harness.Appointments.CreateAsync(
                Request(fixture.CustomerId,
                    services: [new(fixture.ServiceId, fixture.StaffId)],
                    serviceBundleId: Guid.NewGuid())));
    }

    [Fact]
    public async Task A_bundle_that_has_been_taken_off_the_menu_cannot_be_booked()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);

        await using (var context = fixture.Harness.Reopen())
        {
            var bundle = await context.ServiceBundles.SingleAsync(candidate => candidate.Id == bundleId);
            bundle.IsActive = false;
            await context.SaveChangesAsync();
        }

        fixture.Harness.Context.ChangeTracker.Clear();

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.CreateAsync(BundleRequest(fixture, bundleId)));

        Assert.Equal(
            "'Full Makeover' is no longer bookable.",
            Assert.Single(failure.Errors["serviceBundleId"]));
    }

    [Fact]
    public async Task A_bundle_booking_needs_the_whole_component_window_to_be_free()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);

        // Only overlaps the last half hour, which exists because Colour is part
        // of the bundle: the window is the sum of the component durations.
        await fixture.Harness.SeedTimeOffAsync(fixture.StaffId, At(11), At(11, 30), reason: "Break");

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.CreateAsync(BundleRequest(fixture, bundleId)));

        Assert.Contains("Jasmin Rae is not available at the selected time.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Editing_a_bundle_booking_back_to_single_services_clears_the_bundle_snapshot()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var bundleId = await SeedMakeoverBundleAsync(fixture);
        var created = await fixture.Harness.Appointments.CreateAsync(BundleRequest(fixture, bundleId));

        var updated = await fixture.Harness.Appointments.UpdateAsync(
            created.Id,
            Request(fixture.CustomerId, At(13), [new(fixture.ServiceId, fixture.StaffId)]));

        Assert.Null(updated.ServiceBundleId);
        Assert.Null(updated.BundleName);
        Assert.Null(updated.BundlePrice);
        Assert.Equal(At(13, 30), updated.EndsAt);
        Assert.Equal("Haircut", Assert.Single(updated.Services).ServiceName);

        var saved = await ReloadAsync(fixture.Harness, created.Id);

        Assert.Null(saved.ServiceBundleId);
        Assert.Null(saved.BundlePriceSnapshot);
    }

    [Fact]
    public async Task A_terminal_appointment_cannot_be_edited()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var completedId = await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11), AppointmentStatus.Completed, customerId: fixture.CustomerId,
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.UpdateAsync(
                completedId,
                Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)])));

        Assert.Contains("cannot be changed.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Moving_a_booking_keeps_its_duration()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var moved = await fixture.Harness.Appointments.UpdateScheduleAsync(
            created.Id, new UpdateScheduleRequest(StartsAt: At(14), null, null));

        Assert.Equal(At(14), moved.StartsAt);
        Assert.Equal(At(14, 30), moved.EndsAt);
    }

    [Fact]
    public async Task Dragging_horizontally_reassigns_every_line_to_the_new_member()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));
        var nadiaId = await fixture.Harness.SeedStaffAsync("Nadia", "Saleh");
        await fixture.Harness.SeedWorkingHoursAsync(nadiaId, DayOfWeek.Monday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        await fixture.Harness.SeedAssignmentAsync(nadiaId, fixture.ServiceId);

        var moved = await fixture.Harness.Appointments.UpdateScheduleAsync(
            created.Id, new UpdateScheduleRequest(null, StaffId: nadiaId, null));

        Assert.All(moved.Services, line => Assert.Equal(nadiaId, line.StaffId));
        Assert.Equal("Nadia", moved.Services[0].StaffFirstName);
    }

    [Fact]
    public async Task Dragging_onto_a_member_not_assigned_to_every_service_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));
        var strangerId = await fixture.Harness.SeedStaffAsync("Omar", "Faris");

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(null, StaffId: strangerId, null)));

        Assert.Contains(
            failure.Errors["staffId"],
            message => message.Contains("is not assigned to", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resizing_applies_the_delta_to_the_last_line()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services:
            [
                new(fixture.ServiceId, fixture.StaffId),
                new(fixture.SecondServiceId, fixture.StaffId)
            ]));

        var resized = await fixture.Harness.Appointments.UpdateScheduleAsync(
            created.Id, new UpdateScheduleRequest(null, null, DurationMinutes: 120));

        Assert.Equal(120, resized.TotalDurationMinutes);
        Assert.Equal(At(12), resized.EndsAt);
        Assert.Equal(30, resized.Services[0].DurationMinutes);
        Assert.Equal(90, resized.Services[1].DurationMinutes);
    }

    [Fact]
    public async Task Resizing_below_the_minimum_duration_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(null, null, DurationMinutes: 4)));

        Assert.Contains(failure.Errors["durationMinutes"], message => message.Contains("at least 5 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Resizing_that_would_shrink_the_last_line_below_the_floor_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services:
            [
                new(fixture.ServiceId, fixture.StaffId),
                new(fixture.SecondServiceId, fixture.StaffId)
            ]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(null, null, DurationMinutes: 20)));

        Assert.Contains(failure.Errors["durationMinutes"], message => message.Contains("at least 5 minutes", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_move_into_a_busy_slot_is_a_409_and_leaves_the_booking_untouched()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));
        await fixture.Harness.SeedAppointmentAsync(
            At(13), At(14),
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(StartsAt: At(13), null, null)));

        Assert.Contains("is not available at the selected time.", failure.Message, StringComparison.Ordinal);

        var saved = await ReloadAsync(fixture.Harness, created.Id);
        Assert.Equal(At(10), saved.StartsAt);
    }

    [Fact]
    public async Task A_terminal_appointment_cannot_be_moved_or_resized()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var completedId = await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11), AppointmentStatus.Completed, customerId: fixture.CustomerId,
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                completedId, new UpdateScheduleRequest(StartsAt: At(13), null, null)));
    }

    [Fact]
    public async Task An_empty_schedule_patch_says_nothing_to_change()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(null, null, null)));

        Assert.Contains(failure.Errors["startsAt"], message => message.Contains("Nothing to change.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_schedule_patch_into_the_past_is_rejected()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));
        var past = new DateTimeOffset(2026, 10, 3, 8, 0, 0, TimeSpan.Zero);

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.UpdateScheduleAsync(
                created.Id, new UpdateScheduleRequest(StartsAt: past, null, null)));

        Assert.Contains(failure.Errors["startsAt"], message => message.Contains("in the past", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Checking_in_records_the_transition_in_history()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var updated = await fixture.Harness.Appointments.SetStatusAsync(
            created.Id, new SetAppointmentStatusRequest("CheckedIn", null));

        Assert.Equal("CheckedIn", updated.Status);

        var saved = await ReloadAsync(fixture.Harness, created.Id);

        // Both rows share the fake clock's instant, so their order is not
        // guaranteed; select by content rather than by position.
        var history = saved.StatusHistory.ToList();
        Assert.Equal(2, history.Count);
        var booked = history.Single(row => row.FromStatus is null);
        Assert.Equal(AppointmentStatus.Confirmed, booked.ToStatus);
        var checkedIn = history.Single(row => row.FromStatus == AppointmentStatus.Confirmed);
        Assert.Equal(AppointmentStatus.CheckedIn, checkedIn.ToStatus);
    }

    [Fact]
    public async Task Cancelling_captures_the_reason_on_the_appointment_and_the_history_row()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var updated = await fixture.Harness.Appointments.SetStatusAsync(
            created.Id, new SetAppointmentStatusRequest("cancelled", "Client called"));

        Assert.Equal("Cancelled", updated.Status);
        Assert.Equal("Client called", updated.CancellationReason);

        var saved = await ReloadAsync(fixture.Harness, created.Id);
        var row = saved.StatusHistory.Single(history => history.ToStatus == AppointmentStatus.Cancelled);
        Assert.Equal("Client called", row.Reason);
    }

    [Fact]
    public async Task An_illegal_transition_is_a_409()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.SetStatusAsync(
                created.Id, new SetAppointmentStatusRequest("Completed", null)));

        Assert.Contains("cannot move from Confirmed to Completed.", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_terminal_appointment_cannot_change_status()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var completedId = await fixture.Harness.SeedAppointmentAsync(
            At(10), At(11), AppointmentStatus.Completed, customerId: fixture.CustomerId,
            lines: [new SchedulingSeed.Line(fixture.ServiceId, fixture.StaffId, "Haircut")]);

        var failure = await Assert.ThrowsAsync<ConflictException>(() =>
            fixture.Harness.Appointments.SetStatusAsync(
                completedId, new SetAppointmentStatusRequest("Cancelled", null)));

        Assert.Contains("historical record", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_unrecognised_status_is_a_400()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.SetStatusAsync(
                created.Id, new SetAppointmentStatusRequest("Flying", null)));

        Assert.Contains(failure.Errors["status"], message => message.Contains("not a recognised appointment status", StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_missing_status_is_a_400()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.SetStatusAsync(
                created.Id, new SetAppointmentStatusRequest(null, null)));

        Assert.Contains(failure.Errors["status"], message => message.Contains("Choose a status.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_over_long_reason_is_a_400()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;
        var created = await fixture.Harness.Appointments.CreateAsync(
            Request(fixture.CustomerId, services: [new(fixture.ServiceId, fixture.StaffId)]));

        var failure = await Assert.ThrowsAsync<ValidationFailedException>(() =>
            fixture.Harness.Appointments.SetStatusAsync(
                created.Id, new SetAppointmentStatusRequest("Cancelled", new string('x', 501))));

        Assert.Contains(failure.Errors["reason"], message => message.Contains("500 characters or fewer", StringComparison.Ordinal));
    }

    [Fact]
    public async Task An_unknown_appointment_is_a_404()
    {
        var fixture = await ArrangeAsync();
        using var _ = fixture.Harness;

        await Assert.ThrowsAsync<NotFoundException>(() => fixture.Harness.Appointments.GetAsync(Guid.NewGuid()));
    }
}
