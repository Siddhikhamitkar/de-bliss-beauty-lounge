using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Settings;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The customer booking milestone over the real pipeline: the anonymous day
/// grid (only genuinely bookable starts, salon-local, never leaking staff
/// contact details), booking with identity taken from the token and prices
/// taken from the catalogue, replay-safe idempotency, the two-booking race
/// for one slot, and the walls between customers — with the front desk's own
/// booking endpoint unchanged alongside.
/// </summary>
public sealed class CustomerBookingApiTests : ApiTestBase
{
    private const string Password = "Test#Customer123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>The zone the salon configuration resolves to by default: Asia/Dubai, fixed +04.</summary>
    private static readonly TimeZoneInfo SalonZone = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

    /// <summary>
    /// The next occurrence of a weekday in salon time, always strictly in the
    /// future so no slot can be in the past when the test runs.
    /// </summary>
    private static DateOnly NextSalon(DayOfWeek day)
    {
        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SalonZone).DateTime);
        var daysAhead = ((int)day - (int)today.DayOfWeek + 7) % 7;

        return daysAhead == 0 ? today.AddDays(7) : today.AddDays(daysAhead);
    }

    private static DateTimeOffset SalonSlot(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));

        return new DateTimeOffset(local, SalonZone.GetUtcOffset(local));
    }

    private static List<DateTimeOffset> StartsAt(List<AvailabilitySlot> slots) =>
        [.. slots.Select(slot => slot.StartsAt)];

    public CustomerBookingApiTests()
    {
        Factory.SeedRoleAsync(RoleNames.Customer).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Anonymous_availability_lists_only_genuinely_bookable_slots()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = AnonymousClient;

        var response = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var available = await ReadAsync<AvailabilityDay>(response);

        Assert.Equal(serviceId, available.ServiceId);
        Assert.Equal(day, available.Date);

        // Nine-to-seventeen with a one-hour service on a half-hour grid: 9:00
        // through 16:00 inclusive, nothing later fits the full duration.
        Assert.Equal(15, available.Slots.Count);
        Assert.Equal(SalonSlot(day, 9), available.Slots[0].StartsAt);
        Assert.Equal(SalonSlot(day, 16), available.Slots[^1].StartsAt);

        var starts = StartsAt(available.Slots);
        Assert.DoesNotContain(SalonSlot(day, 16, 30), starts);

        Assert.All(available.Slots, slot => Assert.Equal(slot.StartsAt.AddMinutes(60), slot.EndsAt));

        var member = Assert.Single(available.Slots[0].Staff);
        Assert.Equal(staffId, member.StaffId);
        Assert.Equal("Sara Stylist", member.DisplayName);

        // A display name only: the seeded contact details and job title of the
        // staff row must never reach an anonymous caller.
        Assert.DoesNotContain("sara@debliss.test", body);
        Assert.DoesNotContain("+971 50 111 2222", body);
        Assert.DoesNotContain("Senior Stylist", body);
    }

    [Fact]
    public async Task Availability_for_an_unknown_service_is_not_found()
    {
        using var client = AnonymousClient;

        var response = await client.GetAsync($"/api/public/availability?serviceId={Guid.NewGuid()}&date={NextSalon(DayOfWeek.Tuesday):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Availability_refuses_inactive_or_not_online_bookable_services()
    {
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var inactiveId = await Factory.SeedServiceAsync(categoryId, "Retired", 100m, isActive: false, isOnlineBookable: true);
        var internalOnlyId = await Factory.SeedServiceAsync(categoryId, "In-Salon Only", 100m);
        var day = NextSalon(DayOfWeek.Tuesday);
        using var client = AnonymousClient;

        var inactive = await client.GetAsync($"/api/public/availability?serviceId={inactiveId}&date={day:yyyy-MM-dd}");
        var internalOnly = await client.GetAsync($"/api/public/availability?serviceId={internalOnlyId}&date={day:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, inactive.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, internalOnly.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(inactive);

        Assert.Contains(problem.Errors, pair => pair.Key == "serviceId");
    }

    [Fact]
    public async Task Availability_never_offers_a_slot_the_duration_cannot_fit()
    {
        var staffId = await Factory.SeedStaffAsync("Nadia", "Early");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Signature Cut", 120m, 60, staffIds: [staffId], isOnlineBookable: true);
        var day = NextSalon(DayOfWeek.Tuesday);
        await Factory.SeedWorkingHoursAsync(staffId, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(10, 30, 0));
        using var client = AnonymousClient;

        var response = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var available = await ReadAsync<AvailabilityDay>(response);

        // One and a half working hours: exactly two one-hour starts fit.
        Assert.Equal(2, available.Slots.Count);
        Assert.Equal(SalonSlot(day, 9), available.Slots[0].StartsAt);
        Assert.Equal(SalonSlot(day, 9, 30), available.Slots[1].StartsAt);
    }

    [Fact]
    public async Task Availability_removes_time_off_and_blocked_periods()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();

        await using (var db = Factory.Database.CreateContext())
        {
            await StaffingSeed.TimeOffAsync(db, staffId, SalonSlot(day, 11), SalonSlot(day, 13));
            await StaffingSeed.BlockedTimeAsync(db, staffId, SalonSlot(day, 14), SalonSlot(day, 14, 30));
            await StaffingSeed.BlockedTimeAsync(db, staffId: null, SalonSlot(day, 15), SalonSlot(day, 15, 30));
        }

        using var client = AnonymousClient;
        var response = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var available = await ReadAsync<AvailabilityDay>(response);
        var starts = StartsAt(available.Slots);

        // Time off 11:00-13:00 removes 10:30-12:30; the staff break
        // 14:00-14:30 removes 13:30-14:00; the salon-wide block
        // 15:00-15:30 removes 14:30-15:00.
        Assert.Equal(6, available.Slots.Count);
        Assert.Contains(SalonSlot(day, 9), starts);
        Assert.Contains(SalonSlot(day, 9, 30), starts);
        Assert.Contains(SalonSlot(day, 10), starts);
        Assert.Contains(SalonSlot(day, 13), starts);
        Assert.Contains(SalonSlot(day, 15, 30), starts);
        Assert.Contains(SalonSlot(day, 16), starts);
        Assert.DoesNotContain(SalonSlot(day, 10, 30), starts);
        Assert.DoesNotContain(SalonSlot(day, 11), starts);
        Assert.DoesNotContain(SalonSlot(day, 12), starts);
        Assert.DoesNotContain(SalonSlot(day, 13, 30), starts);
        Assert.DoesNotContain(SalonSlot(day, 14, 30), starts);
        Assert.DoesNotContain(SalonSlot(day, 15), starts);
    }

    [Fact]
    public async Task Availability_removes_the_slot_an_existing_appointment_holds()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        var customerId = await Factory.SeedCustomerAsync("Amina", "Rahal");
        await Factory.SeedCustomerAppointmentAsync(
            customerId,
            SalonSlot(day, 10),
            AppointmentStatus.Confirmed,
            [(serviceId, staffId, "Signature Cut")]);
        using var client = AnonymousClient;

        var response = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={day:yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var available = await ReadAsync<AvailabilityDay>(response);
        var starts = StartsAt(available.Slots);

        Assert.Equal(12, available.Slots.Count);
        Assert.Contains(SalonSlot(day, 9), starts);
        Assert.DoesNotContain(SalonSlot(day, 9, 30), starts);
        Assert.DoesNotContain(SalonSlot(day, 10), starts);
        Assert.DoesNotContain(SalonSlot(day, 10, 30), starts);
        Assert.Contains(SalonSlot(day, 11), starts);
    }

    [Fact]
    public async Task Availability_refuses_past_days_and_the_far_horizon_but_serves_the_boundary()
    {
        var staffId = await Factory.SeedStaffAsync("Yara", "Always");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Signature Cut", 120m, 60, staffIds: [staffId], isOnlineBookable: true);

        foreach (var day in Enum.GetValues<DayOfWeek>())
        {
            await Factory.SeedWorkingHoursAsync(staffId, day, TimeSpan.Zero, TimeSpan.FromHours(24));
        }

        var today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(DateTimeOffset.UtcNow, SalonZone).DateTime);
        using var client = AnonymousClient;

        var past = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={today.AddDays(-1):yyyy-MM-dd}");
        var beyond = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={today.AddDays(92):yyyy-MM-dd}");
        var horizon = PublicAvailabilityService.MaximumDaysAhead;
        var boundary = await client.GetAsync($"/api/public/availability?serviceId={serviceId}&date={today.AddDays(horizon):yyyy-MM-dd}");

        Assert.Equal(HttpStatusCode.BadRequest, past.StatusCode);

        var pastProblem = await ReadAsync<ValidationProblem>(past);

        Assert.Contains(pastProblem.Errors, pair => pair.Key == "date");

        Assert.Equal(HttpStatusCode.BadRequest, beyond.StatusCode);
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);

        var available = await ReadAsync<AvailabilityDay>(boundary);

        // Round-the-clock staffing: a one-hour service fills midnight to
        // midnight with 47 half-hour starts (23:30 cannot fit).
        Assert.Equal(47, available.Slots.Count);
        Assert.Equal(SalonSlot(today.AddDays(horizon), 0), available.Slots[0].StartsAt);
        Assert.Equal(SalonSlot(today.AddDays(horizon), 23), available.Slots[^1].StartsAt);
    }

    [Fact]
    public async Task A_customer_books_an_available_slot_at_the_catalogue_price()
    {
        await SetTaxSettingsAsync(5m, pricesIncludeTax: false);
        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("booker@debliss.test");
        var customerId = await CustomerIdOfAsync(client);

        var response = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 11) }, "book-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var confirmation = await ReadAsync<BookingConfirmation>(response);

        Assert.NotEqual(Guid.Empty, confirmation.AppointmentId);
        Assert.Equal("Signature Cut", confirmation.ServiceName);
        Assert.Equal(SalonSlot(day, 11), confirmation.StartsAt);
        Assert.Equal(SalonSlot(day, 11).AddMinutes(60), confirmation.EndsAt);
        Assert.Equal("Confirmed", confirmation.Status);
        Assert.Equal("Sara Stylist", confirmation.StaffDisplayName);
        Assert.Equal(120m, confirmation.Price);
        Assert.Equal("Online", confirmation.Source);

        // Catalogue prices are VAT-exclusive: the salon settings' 5% is added
        // on top, quoted by Core itself so the customer confirms the exact
        // checkout figure.
        Assert.Equal(5m, confirmation.TaxRate);
        Assert.False(confirmation.PricesIncludeTax);
        Assert.Equal(6.00m, confirmation.TaxAmount);
        Assert.Equal(126.00m, confirmation.TotalPayable);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Appointments
            .Include(appointment => appointment.Services)
            .SingleAsync(appointment => appointment.Id == confirmation.AppointmentId);

        Assert.Equal(customerId, stored.CustomerId);
        Assert.Equal(AppointmentSource.Online, stored.Source);

        var line = Assert.Single(stored.Services);

        Assert.Equal("Signature Cut", line.ServiceNameSnapshot);
        Assert.Equal(60, line.DurationMinutes);
        Assert.Equal(120m, line.UnitPrice);
        Assert.Equal(staffId, line.StaffId);
    }

    [Fact]
    public async Task Booking_without_a_staff_preference_resolves_any_available_staff()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("anystaff@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, startsAt = SalonSlot(day, 10) }, "any-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var confirmation = await ReadAsync<BookingConfirmation>(response);

        Assert.Equal("Sara Stylist", confirmation.StaffDisplayName);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Appointments.Include(appointment => appointment.Services).SingleAsync(appointment => appointment.Id == confirmation.AppointmentId);

        Assert.Equal(staffId, Assert.Single(stored.Services).StaffId);
    }

    [Fact]
    public async Task Booking_a_slot_that_is_already_taken_conflicts()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        var customerId = await Factory.SeedCustomerAsync("Amina", "Rahal");
        await Factory.SeedCustomerAppointmentAsync(
            customerId,
            SalonSlot(day, 10),
            AppointmentStatus.Confirmed,
            [(serviceId, staffId, "Signature Cut")]);
        using var client = await RegisterCustomerAsync("latecomer@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 10, 30) }, "taken-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await ReadAsync<Problem>(response);
    }

    [Fact]
    public async Task Booking_any_available_when_no_staff_is_free_conflicts()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        var customerId = await Factory.SeedCustomerAsync("Amina", "Rahal");
        await Factory.SeedCustomerAppointmentAsync(
            customerId,
            SalonSlot(day, 10),
            AppointmentStatus.Confirmed,
            [(serviceId, staffId, "Signature Cut")]);
        using var client = await RegisterCustomerAsync("nobody@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, startsAt = SalonSlot(day, 10) }, "none-1");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task Booking_with_a_staff_member_not_assigned_to_the_service_is_refused()
    {
        var (_, serviceId, day) = await ArrangeAsync();
        var outsiderId = await Factory.SeedStaffAsync("Outsider", "Omar");
        await Factory.SeedWorkingHoursAsync(outsiderId, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        using var client = await RegisterCustomerAsync("picky@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, staffId = outsiderId, startsAt = SalonSlot(day, 10) }, "picky-1");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Booking_an_unknown_staff_member_is_not_found()
    {
        var (_, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("ghost@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, staffId = Guid.NewGuid(), startsAt = SalonSlot(day, 10) }, "ghost-1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Booking_never_honours_a_customer_id_sent_in_the_body()
    {
        var (_, serviceId, day) = await ArrangeAsync();
        var otherCustomerId = await Factory.SeedCustomerAsync("Other", "Person");
        using var client = await RegisterCustomerAsync("honest@debliss.test");
        var customerId = await CustomerIdOfAsync(client);

        var response = await PostBookingAsync(
            client,
            new { customerId = otherCustomerId, serviceId, startsAt = SalonSlot(day, 10) },
            "spoof-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var confirmation = await ReadAsync<BookingConfirmation>(response);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Appointments.SingleAsync(appointment => appointment.Id == confirmation.AppointmentId);

        Assert.Equal(customerId, stored.CustomerId);
        Assert.NotEqual(otherCustomerId, stored.CustomerId);
    }

    [Fact]
    public async Task History_shows_only_the_signed_in_customers_own_appointments()
    {
        await SetTaxSettingsAsync(5m, pricesIncludeTax: false);
        var (staffId, serviceId, day) = await ArrangeAsync();
        var strangerId = await Factory.SeedCustomerAsync("Stranger", "Person");
        await Factory.SeedCustomerAppointmentAsync(
            strangerId,
            SalonSlot(day, 9),
            AppointmentStatus.Confirmed,
            [(serviceId, staffId, "Signature Cut")]);

        await using (var db = Factory.Database.CreateContext())
        {
            var strangerAppointment = await db.Appointments.SingleAsync(appointment => appointment.CustomerId == strangerId);
            strangerAppointment.Notes = "Internal: prefers the window chair";
            await db.SaveChangesAsync();
        }

        using var client = await RegisterCustomerAsync("historian@debliss.test");
        var booking = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 11) }, "history-1");
        var confirmation = await ReadAsync<BookingConfirmation>(booking);

        var response = await client.GetAsync("/api/customer/appointments");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var history = await ReadAsync<List<HistoryItem>>(response);
        var mine = Assert.Single(history);

        Assert.Equal(confirmation.AppointmentId, mine.Id);
        Assert.Equal(SalonSlot(day, 11), mine.StartsAt);
        Assert.Equal("Confirmed", mine.Status);
        Assert.Equal(120m, mine.TotalPrice);
        Assert.Equal(60, mine.TotalDurationMinutes);
        Assert.Equal(6.00m, mine.TaxAmount);
        Assert.Equal(126.00m, mine.TotalPayable);

        var line = Assert.Single(mine.Services);

        Assert.Equal("Signature Cut", line.ServiceName);
        Assert.Equal("Sara Stylist", line.StaffDisplayName);

        // The stranger's booking, its time and its internal note are absent —
        // so is any note field at all.
        Assert.DoesNotContain(strangerId.ToString(), body);
        Assert.DoesNotContain("window chair", body);
        Assert.DoesNotContain("notes", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_repeated_request_with_the_same_key_returns_the_original_booking()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("replayer@debliss.test");
        var body = new { serviceId, staffId, startsAt = SalonSlot(day, 11) };

        var first = await PostBookingAsync(client, body, "steady-key");
        var second = await PostBookingAsync(client, body, "steady-key");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        var original = await ReadAsync<BookingConfirmation>(first);
        var replayed = await ReadAsync<BookingConfirmation>(second);

        Assert.Equal(original.AppointmentId, replayed.AppointmentId);
        Assert.Equal(original.Price, replayed.Price);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Appointments.ToListAsync();

        Assert.Equal(1, stored.Count(appointment => appointment.StartsAt == SalonSlot(day, 11)));

        var claim = Assert.Single(await db.BookingClaims.ToListAsync());

        Assert.Equal(original.AppointmentId, claim.AppointmentId);
    }

    [Fact]
    public async Task The_same_key_with_a_different_payload_is_refused()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("switcher@debliss.test");

        var first = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 11) }, "two-faced");
        var second = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 12) }, "two-faced");

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Booking_without_an_idempotency_key_is_refused()
    {
        var (_, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("keyless@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, startsAt = SalonSlot(day, 10) }, idempotencyKey: null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains(problem.Errors, pair => pair.Key == "idempotencyKey");
    }

    [Fact]
    public async Task Two_simultaneous_bookings_cannot_hold_the_same_slot()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        var start = SalonSlot(day, 11);
        using var customerA = await RegisterCustomerAsync("racer-a@debliss.test");
        using var customerB = await RegisterCustomerAsync("racer-b@debliss.test");

        var responses = await Task.WhenAll(
            PostBookingAsync(customerA, new { serviceId, staffId, startsAt = start }, "race-a"),
            PostBookingAsync(customerB, new { serviceId, staffId, startsAt = start }, "race-b"));

        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Created));
        Assert.Equal(1, responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Appointments.Include(appointment => appointment.Services).ToListAsync();
        var overlapping = stored
            .Where(appointment => appointment.StartsAt < start.AddMinutes(60) && appointment.EndsAt > start)
            .ToList();

        Assert.Single(overlapping);
    }

    [Fact]
    public async Task The_front_desk_books_through_its_own_endpoint_unchanged()
    {
        var (staffId, serviceId, day) = await ArrangeAsync();
        var customerId = await Factory.SeedCustomerAsync("Walked", "In");
        var start = SalonSlot(day, 10);

        var response = await Client.PostAsJsonAsync("/api/appointments", new
        {
            customerId,
            walkInCustomerName = (string?)null,
            walkInCustomerPhone = (string?)null,
            startsAt = start,
            services = new[] { new { serviceId, staffId } },
        }, Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var created = await ReadAsync<AppointmentDto>(response);

        Assert.Equal("Reception", created.Source);
        Assert.Equal(customerId, created.CustomerId);
    }

    [Fact]
    public async Task Customer_booking_surfaces_require_authentication()
    {
        using var client = AnonymousClient;
        var (_, serviceId, day) = await ArrangeAsync();

        var read = await client.GetAsync("/api/customer/appointments");
        var write = await PostBookingAsync(client, new { serviceId, startsAt = SalonSlot(day, 10) }, "anon-1");

        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, write.StatusCode);
    }

    /// <summary>
    /// Pins the salon tax settings for the test. The API quotes whatever rate
    /// is configured, so a VAT expectation is only meaningful when the
    /// arrangement owns the configuration rather than inheriting whatever the
    /// factory database holds.
    /// </summary>
    private async Task SetTaxSettingsAsync(decimal rate, bool pricesIncludeTax)
    {
        await using var db = Factory.Database.CreateContext();
        var settings = await db.SalonSettings.FirstOrDefaultAsync();

        if (settings is null)
        {
            db.SalonSettings.Add(new SalonSettings
            {
                Id = SalonSettings.SingletonId,
                TaxRate = rate,
                PricesIncludeTax = pricesIncludeTax,
            });
        }
        else
        {
            settings.TaxRate = rate;
            settings.PricesIncludeTax = pricesIncludeTax;
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task The_quoted_VAT_is_rounded_to_fils_away_from_zero()
    {
        await SetTaxSettingsAsync(5m, pricesIncludeTax: false);
        var staffId = await Factory.SeedStaffAsync("Rehana", "Rounding");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(
            categoryId,
            "Precision Trim",
            33.35m,
            45,
            staffIds: [staffId],
            isOnlineBookable: true);
        var day = NextSalon(DayOfWeek.Tuesday);
        await Factory.SeedWorkingHoursAsync(staffId, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));
        using var client = await RegisterCustomerAsync("rounding@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 11) }, "round-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var confirmation = await ReadAsync<BookingConfirmation>(response);

        Assert.Equal(33.35m, confirmation.Price);

        // 33.35 × 5% = 1.6675 — quoted at 1.67, so the customer confirms 35.02,
        // the same figure the checkout's TaxMath would charge.
        Assert.Equal(1.67m, confirmation.TaxAmount);
        Assert.Equal(35.02m, confirmation.TotalPayable);
    }

    [Fact]
    public async Task An_inclusive_price_configuration_quotes_the_tax_inside_the_total()
    {
        await SetTaxSettingsAsync(5m, pricesIncludeTax: true);

        var (staffId, serviceId, day) = await ArrangeAsync();
        using var client = await RegisterCustomerAsync("inclusive@debliss.test");

        var response = await PostBookingAsync(client, new { serviceId, staffId, startsAt = SalonSlot(day, 11) }, "inclusive-1");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var confirmation = await ReadAsync<BookingConfirmation>(response);

        // The 120 price already contains VAT: the tax portion is 120 × 5/105
        // = 5.71 and what the customer pays stays 120.
        Assert.True(confirmation.PricesIncludeTax);
        Assert.Equal(5.71m, confirmation.TaxAmount);
        Assert.Equal(120m, confirmation.TotalPayable);
    }

    private async Task<(Guid StaffId, Guid ServiceId, DateOnly Day)> ArrangeAsync()
    {
        var staffId = await Factory.SeedStaffAsync(
            "Sara",
            "Stylist",
            jobTitle: "Senior Stylist",
            email: "sara@debliss.test",
            calendarColor: "#3366ff");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(
            categoryId,
            "Signature Cut",
            120m,
            60,
            staffIds: [staffId],
            isOnlineBookable: true);
        var day = NextSalon(DayOfWeek.Tuesday);

        await Factory.SeedWorkingHoursAsync(staffId, DayOfWeek.Tuesday, new TimeSpan(9, 0, 0), new TimeSpan(17, 0, 0));

        await using (var db = Factory.Database.CreateContext())
        {
            var member = await db.Staff.SingleAsync(row => row.Id == staffId);
            member.PhoneNumber = "+971 50 111 2222";
            await db.SaveChangesAsync();
        }

        return (staffId, serviceId, day);
    }

    private async Task<HttpClient> RegisterCustomerAsync(string email)
    {
        using var registration = AnonymousClient;

        var response = await registration.PostAsJsonAsync("/api/public/customers/register", new
        {
            firstName = "Test",
            lastName = "Customer",
            email,
            password = Password,
        }, Json);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Registration returned {(int)response.StatusCode}: {errorBody}");
        }

        var client = AnonymousClient;
        await AuthenticateAsync(client, email, Password);

        return client;
    }

    private async Task<Guid> CustomerIdOfAsync(HttpClient client)
    {
        using var me = await client.GetAsync("/api/customer/me");

        me.EnsureSuccessStatusCode();

        return (await ReadAsync<CustomerSelfDto>(me)).Id;
    }

    private static Task<HttpResponseMessage> PostBookingAsync<T>(HttpClient client, T body, string? idempotencyKey)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, "/api/customer/appointments")
        {
            Content = new StringContent(JsonSerializer.Serialize(body, Json), Encoding.UTF8, "application/json"),
        };

        if (idempotencyKey is not null)
        {
            message.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return client.SendAsync(message);
    }

    private sealed record AvailabilityStaff(Guid StaffId, string DisplayName);

    private sealed record AvailabilitySlot(DateTimeOffset StartsAt, DateTimeOffset EndsAt, List<AvailabilityStaff> Staff);

    private sealed record AvailabilityDay(Guid ServiceId, DateOnly Date, List<AvailabilitySlot> Slots);

    private sealed record BookingConfirmation(
        Guid AppointmentId,
        string ServiceName,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        string Status,
        string? StaffDisplayName,
        decimal Price,
        decimal TaxRate,
        bool PricesIncludeTax,
        decimal TaxAmount,
        decimal TotalPayable,
        string Source);

    private sealed record HistoryLine(string ServiceName, int DurationMinutes, decimal UnitPrice, string? StaffDisplayName);

    private sealed record HistoryItem(
        Guid Id,
        DateTimeOffset StartsAt,
        DateTimeOffset EndsAt,
        string Status,
        List<HistoryLine> Services,
        decimal TotalPrice,
        decimal TaxRate,
        bool PricesIncludeTax,
        decimal TaxAmount,
        decimal TotalPayable,
        int TotalDurationMinutes);
}
