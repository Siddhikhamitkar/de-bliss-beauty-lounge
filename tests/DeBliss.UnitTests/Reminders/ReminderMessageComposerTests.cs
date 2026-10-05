using DeBliss.Application.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Reminders;

/// <summary>
/// The template values a WhatsApp message is built from. These strings are read
/// by customers, so dates, staff and service lists are pinned down here: a
/// bundle advertises the bundle name instead of dumping its components, and
/// unassigned parts fall back to wording the approved templates can carry.
/// </summary>
public sealed class ReminderMessageComposerTests
{
    private const string Salon = "DeBliss";

    private static readonly DateTimeOffset Monday10 = new(2026, 10, 5, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid AppointmentId = Guid.Parse("a1100000-0000-4000-8000-000000000001");

    private static ReminderMessageComposer Composer() => new(Salon, new FakeTimeProvider(Monday10));

    private static Appointment AppointmentWith(
        DateTimeOffset startsAt,
        params (string ServiceName, string? StaffFirstName)[] lines)
    {
        var appointment = new Appointment
        {
            Id = AppointmentId,
            StartsAt = startsAt,
            Status = AppointmentStatus.Confirmed
        };

        var sortOrder = 0;

        foreach (var (serviceName, staffFirstName) in lines)
        {
            appointment.Services.Add(new AppointmentService
            {
                ServiceNameSnapshot = serviceName,
                SortOrder = sortOrder++,
                Staff = staffFirstName is null
                    ? null
                    : new Staff { FirstName = staffFirstName, LastName = "Rae" }
            });
        }

        return appointment;
    }

    [Fact]
    public void Compose_fills_every_template_variable()
    {
        var values = Composer().Compose(
            AppointmentWith(Monday10, ("Haircut", "Jasmin"), ("Colour", "Maya")),
            "Sara Haddad");

        Assert.Equal("Sara Haddad", values[ReminderTemplateVariables.CustomerName]);
        Assert.Equal(Salon, values[ReminderTemplateVariables.SalonName]);
        Assert.Equal("Mon, 05 Oct 2026", values[ReminderTemplateVariables.AppointmentDate]);
        Assert.Equal("10:00", values[ReminderTemplateVariables.AppointmentTime]);
        Assert.Equal("Jasmin, Maya", values[ReminderTemplateVariables.StaffName]);
        Assert.Equal("Haircut, Colour", values[ReminderTemplateVariables.ServiceName]);
        Assert.Equal(AppointmentId.ToString(), values[ReminderTemplateVariables.AppointmentId]);
    }

    [Fact]
    public void Compose_dedupes_repeated_staff_and_service_names()
    {
        var values = Composer().Compose(
            AppointmentWith(Monday10, ("Haircut", "Jasmin"), ("haircut", "jasmin")),
            "Sara Haddad");

        Assert.Equal("Jasmin", values[ReminderTemplateVariables.StaffName]);
        Assert.Equal("Haircut", values[ReminderTemplateVariables.ServiceName]);
    }

    [Fact]
    public void Compose_writes_the_time_in_24_hour_format()
    {
        var values = Composer().Compose(
            AppointmentWith(new DateTimeOffset(2026, 10, 5, 14, 30, 0, TimeSpan.Zero), ("Haircut", "Jasmin")),
            "Sara Haddad");

        Assert.Equal("14:30", values[ReminderTemplateVariables.AppointmentTime]);
    }

    [Fact]
    public void Compose_falls_back_to_the_team_when_no_staff_is_assigned()
    {
        var values = Composer().Compose(
            AppointmentWith(Monday10, ("Haircut", null)),
            "Sara Haddad");

        Assert.Equal("our team", values[ReminderTemplateVariables.StaffName]);
    }

    [Fact]
    public void Compose_falls_back_when_there_are_no_services_at_all()
    {
        var values = Composer().Compose(AppointmentWith(Monday10), "Sara Haddad");

        Assert.Equal("our team", values[ReminderTemplateVariables.StaffName]);
        Assert.Equal("your appointment", values[ReminderTemplateVariables.ServiceName]);
    }

    [Fact]
    public void Compose_prefers_the_bundle_name_over_the_component_services()
    {
        var appointment = AppointmentWith(Monday10, ("Haircut", "Jasmin"), ("Colour", "Maya"));
        appointment.ServiceBundle = new ServiceBundle { Name = "Hair Makeover" };

        var values = Composer().Compose(appointment, "Sara Haddad");

        Assert.Equal("Hair Makeover", values[ReminderTemplateVariables.ServiceName]);
        Assert.Equal("Jasmin, Maya", values[ReminderTemplateVariables.StaffName]);
    }

    [Fact]
    public void ComposeTest_marks_every_value_as_a_sample()
    {
        var values = Composer().ComposeTest();

        Assert.Equal("Test Message (ignore)", values[ReminderTemplateVariables.CustomerName]);
        Assert.Equal(Salon, values[ReminderTemplateVariables.SalonName]);
        Assert.Equal("Mon, 05 Oct 2026", values[ReminderTemplateVariables.AppointmentDate]);
        Assert.Equal("11:00", values[ReminderTemplateVariables.AppointmentTime]);
        Assert.Equal("Test staff", values[ReminderTemplateVariables.StaffName]);
        Assert.Equal("Test service", values[ReminderTemplateVariables.ServiceName]);
        Assert.Equal("test", values[ReminderTemplateVariables.AppointmentId]);
    }
}
