using DeBliss.Domain.Customers;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Api.DevData;

/// <summary>
/// Development-only scenario that lets the calendar UI be exercised at realistic
/// density: five staff with different working patterns, a full service menu, and
/// a week of appointments covering every status, back-to-back and overlapping
/// bookings, breaks, time off and a walk-in. It runs only when the hosting
/// environment is Development and only while the staff table is empty, so it
/// never touches a real salon database and never re-seeds over data reception
/// created themselves.
/// </summary>
public static class DevDataSeeder
{
    public static async Task SeedAsync(WebApplication app)
    {
        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeBlissDbContext>();

        if (await db.Staff.AnyAsync())
        {
            return;
        }

        var now = DateTimeOffset.Now;
        var today = now.Date;
        DateTimeOffset At(int dayOffset, int hour, int minute) =>
            new(today.AddDays(dayOffset).AddHours(hour).AddMinutes(minute), now.Offset);

        var hair = new ServiceCategory { Name = "Hair", SortOrder = 0 };
        var color = new ServiceCategory { Name = "Color", SortOrder = 1 };
        var treatments = new ServiceCategory { Name = "Treatments", SortOrder = 2 };
        var nails = new ServiceCategory { Name = "Nails", SortOrder = 3 };
        db.ServiceCategories.AddRange(hair, color, treatments, nails);

        var cut = new Service { Name = "Signature Cut", DurationMinutes = 45, Price = 150m, ServiceCategory = hair, SortOrder = 0 };
        var blowdry = new Service { Name = "Blowdry Style", DurationMinutes = 30, Price = 100m, ServiceCategory = hair, SortOrder = 1 };
        var cutBlowdry = new Service { Name = "Cut + Blowdry", DurationMinutes = 75, Price = 220m, ServiceCategory = hair, SortOrder = 2 };
        var fullColor = new Service { Name = "Full Color", DurationMinutes = 90, Price = 350m, ServiceCategory = color, SortOrder = 0 };
        var balayage = new Service { Name = "Balayage", DurationMinutes = 120, Price = 550m, ServiceCategory = color, SortOrder = 1 };
        var rootTouchUp = new Service { Name = "Root Touch-Up", DurationMinutes = 60, Price = 220m, ServiceCategory = color, SortOrder = 2 };
        var keratin = new Service { Name = "Keratin Smoothing", DurationMinutes = 90, Price = 600m, ServiceCategory = treatments, SortOrder = 0 };
        var hairSpa = new Service { Name = "Hair Spa Ritual", DurationMinutes = 60, Price = 300m, ServiceCategory = treatments, SortOrder = 1 };
        var manicure = new Service { Name = "Classic Manicure", DurationMinutes = 45, Price = 120m, ServiceCategory = nails, SortOrder = 0 };
        var pedicure = new Service { Name = "Gel Pedicure", DurationMinutes = 60, Price = 180m, ServiceCategory = nails, SortOrder = 1 };
        db.Services.AddRange(cut, blowdry, cutBlowdry, fullColor, balayage, rootTouchUp, keratin, hairSpa, manicure, pedicure);

        var aisha = new Staff { FirstName = "Aisha", LastName = "Al Mansouri", JobTitle = "Color Director", CalendarColor = "#8a4f56", SortOrder = 0 };
        var sofia = new Staff { FirstName = "Sofia", LastName = "Reyes", JobTitle = "Senior Stylist", CalendarColor = "#b08968", SortOrder = 1 };
        var layla = new Staff { FirstName = "Layla", LastName = "Haddad", JobTitle = "Stylist", CalendarColor = "#7d6b9e", SortOrder = 2 };
        var maria = new Staff { FirstName = "Maria", LastName = "Santos", JobTitle = "Nail Technician", CalendarColor = "#6b8e7f", SortOrder = 3 };
        var omar = new Staff { FirstName = "Omar", LastName = "Farouk", JobTitle = "Barber", CalendarColor = "#4f6d8a", SortOrder = 4 };
        db.Staff.AddRange(aisha, sofia, layla, maria, omar);

        // Dubai salon week: Sunday to Thursday full days, Friday afternoons,
        // Saturday late morning onwards. Days off are staggered: Sofia Thursday,
        // Aisha and Layla Friday, Maria Saturday. Off days get no row at all —
        // the calendar treats an absent day as non-working.
        Working(aisha, DayOfWeek.Sunday, 9, 19);
        Working(aisha, DayOfWeek.Monday, 9, 19);
        Working(aisha, DayOfWeek.Tuesday, 9, 19);
        Working(aisha, DayOfWeek.Wednesday, 9, 19);
        Working(aisha, DayOfWeek.Thursday, 9, 19);
        Working(aisha, DayOfWeek.Saturday, 11, 19);

        Working(sofia, DayOfWeek.Sunday, 9, 18);
        Working(sofia, DayOfWeek.Monday, 9, 18);
        Working(sofia, DayOfWeek.Tuesday, 9, 18);
        Working(sofia, DayOfWeek.Wednesday, 9, 18);
        Working(sofia, DayOfWeek.Friday, 14, 21);
        Working(sofia, DayOfWeek.Saturday, 10, 19);

        Working(layla, DayOfWeek.Sunday, 10, 19);
        Working(layla, DayOfWeek.Monday, 10, 19);
        Working(layla, DayOfWeek.Tuesday, 10, 19);
        Working(layla, DayOfWeek.Wednesday, 10, 19);
        Working(layla, DayOfWeek.Thursday, 10, 19);
        Working(layla, DayOfWeek.Saturday, 10, 19);

        Working(maria, DayOfWeek.Sunday, 9, 18);
        Working(maria, DayOfWeek.Monday, 9, 18);
        Working(maria, DayOfWeek.Tuesday, 9, 18);
        Working(maria, DayOfWeek.Wednesday, 9, 18);
        Working(maria, DayOfWeek.Thursday, 9, 18);
        Working(maria, DayOfWeek.Friday, 15, 21);

        Working(omar, DayOfWeek.Sunday, 11, 20);
        Working(omar, DayOfWeek.Monday, 11, 20);
        Working(omar, DayOfWeek.Tuesday, 11, 20);
        Working(omar, DayOfWeek.Wednesday, 11, 20);
        Working(omar, DayOfWeek.Thursday, 11, 20);
        Working(omar, DayOfWeek.Friday, 15, 21);
        Working(omar, DayOfWeek.Saturday, 12, 21);

        void Working(Staff member, DayOfWeek day, int start, int end)
        {
            db.StaffWorkingHours.Add(new StaffWorkingHour
            {
                Staff = member,
                DayOfWeek = day,
                IsWorkingDay = true,
                StartTime = TimeSpan.FromHours(start),
                EndTime = TimeSpan.FromHours(end),
            });
        }

        db.StaffServices.AddRange(
            Can(aisha, fullColor), Can(aisha, balayage), Can(aisha, rootTouchUp), Can(aisha, keratin),
            Can(sofia, cut), Can(sofia, blowdry), Can(sofia, cutBlowdry), Can(sofia, hairSpa),
            Can(layla, cut), Can(layla, blowdry), Can(layla, cutBlowdry), Can(layla, hairSpa),
            Can(maria, manicure), Can(maria, pedicure),
            Can(omar, cut), Can(omar, cutBlowdry));
        StaffService Can(Staff member, Service service) => new() { Staff = member, Service = service };

        Customer Customer(string first, string last, string phone) => new()
        {
            FirstName = first,
            LastName = last,
            PhoneNumber = phone,
            Email = null,
        };

        var huda = Customer("Huda", "Al Falasi", "+971 50 123 4561");
        var charlotte = Customer("Charlotte", "Reid", "+971 55 234 5672");
        var nadia = Customer("Nadia", "Al Zaabi", "+971 52 345 6783");
        var mariam = Customer("Mariam", "Al Suwaidi", "+971 56 456 7894");
        var ivy = Customer("Ivy", "Chen", "+971 50 567 8905");
        var fatima = Customer("Fatima", "Al Rashid", "+971 55 678 9016");
        var dana = Customer("Dana", "Mansour", "+971 52 789 0127");
        var olivia = Customer("Olivia", "Grant", "+971 56 890 1238");
        var sara = Customer("Sara", "Khan", "+971 50 901 2349");
        db.Customers.AddRange(huda, charlotte, nadia, mariam, ivy, fatima, dana, olivia, sara);

        // Today's board: a busy mid-morning across three columns, back-to-back
        // bookings on Sofia, a deliberate overlap on Layla to exercise lane
        // splitting, and a walk-in on Omar.
        Appointment Book(
            Customer? customer,
            int dayOffset,
            int hour,
            int minute,
            Service service,
            Staff member,
            AppointmentStatus? status = null,
            AppointmentSource source = AppointmentSource.Phone,
            string? notes = null,
            string? cancellationReason = null,
            string? walkInName = null) =>
            new()
            {
                Customer = customer,
                WalkInCustomerName = walkInName,
                StartsAt = At(dayOffset, hour, minute),
                EndsAt = At(dayOffset, hour, minute).AddMinutes(service.DurationMinutes),
                Status = status ?? RealisticStatus(At(dayOffset, hour, minute), service.DurationMinutes, now),
                Source = source,
                Notes = notes,
                CancellationReason = cancellationReason,
                Services =
                [
                    new AppointmentService
                    {
                        Service = service,
                        Staff = member,
                        ServiceNameSnapshot = service.Name,
                        DurationMinutes = service.DurationMinutes,
                        UnitPrice = service.Price,
                        SortOrder = 0,
                    },
                ],
            };

        db.Appointments.AddRange(
            Book(huda, 0, 9, 0, fullColor, aisha, notes: "Sensitive scalp — patch test done"),
            Book(charlotte, 0, 11, 30, balayage, aisha),
            Book(nadia, 0, 15, 0, rootTouchUp, aisha),
            Book(mariam, 0, 10, 0, cutBlowdry, sofia),
            Book(ivy, 0, 11, 15, blowdry, sofia),
            Book(fatima, 0, 13, 0, cut, layla),
            Book(dana, 0, 13, 30, hairSpa, layla),
            Book(ivy, 0, 10, 0, manicure, maria),
            Book(olivia, 0, 14, 0, pedicure, maria),
            Book(null, 0, 11, 0, cut, omar, source: AppointmentSource.WalkIn, walkInName: "Walk-in Guest"),
            Book(sara, 0, 16, 30, cut, omar),
            Book(mariam, -1, 11, 0, cutBlowdry, sofia, AppointmentStatus.Completed),
            Book(huda, -1, 13, 0, fullColor, aisha, AppointmentStatus.Completed),
            Book(sara, -1, 12, 0, cut, omar, AppointmentStatus.NoShow),
            Book(dana, -1, 17, 0, cut, layla, AppointmentStatus.Cancelled, cancellationReason: "Client asked to move to next week"),
            Book(charlotte, 1, 11, 30, balayage, aisha),
            Book(olivia, 1, 12, 0, pedicure, maria),
            Book(ivy, 1, 14, 0, cutBlowdry, sofia));

        db.BlockedTime.AddRange(
            new BlockedTime { Staff = null, StartsAt = At(0, 12, 0), EndsAt = At(0, 12, 30), Type = BlockedTimeType.Meeting, Reason = "Team huddle" },
            new BlockedTime { Staff = aisha, StartsAt = At(0, 16, 30), EndsAt = At(0, 17, 30), Type = BlockedTimeType.Break, Reason = "Lunch" },
            new BlockedTime { Staff = omar, StartsAt = At(0, 13, 0), EndsAt = At(0, 14, 0), Type = BlockedTimeType.Break, Reason = "Lunch" },
            new BlockedTime { Staff = null, StartsAt = At(1, 9, 30), EndsAt = At(1, 11, 0), Type = BlockedTimeType.Training, Reason = "Colour workshop" });

        db.StaffTimeOff.Add(new StaffTimeOff
        {
            Staff = layla,
            StartsAt = At(1, 0, 0),
            EndsAt = At(1, 23, 59),
            Type = TimeOffType.Leave,
            Reason = "Personal day",
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// Boards read believably no matter when the seeder runs: the past is
    /// completed, the current hour is in progress or checked in, and the rest
    /// of the day is confirmed or still pending.
    /// </summary>
    private static AppointmentStatus RealisticStatus(DateTimeOffset start, int durationMinutes, DateTimeOffset now)
    {
        var end = start.AddMinutes(durationMinutes);

        if (end <= now)
        {
            return AppointmentStatus.Completed;
        }

        if (start <= now)
        {
            return AppointmentStatus.InProgress;
        }

        if (start <= now.AddMinutes(45))
        {
            return AppointmentStatus.CheckedIn;
        }

        return start <= now.AddHours(3) ? AppointmentStatus.Confirmed : AppointmentStatus.Pending;
    }
}
