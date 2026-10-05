using System.Globalization;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Notifications;

/// <summary>
/// Template variable names shared with the placeholder definitions on the
/// WhatsApp side. Keep these in sync with the approved templates.
/// </summary>
public static class ReminderTemplateVariables
{
    public const string CustomerName = "CustomerName";
    public const string SalonName = "SalonName";
    public const string AppointmentDate = "AppointmentDate";
    public const string AppointmentTime = "AppointmentTime";
    public const string StaffName = "StaffName";
    public const string ServiceName = "ServiceName";
    public const string AppointmentId = "AppointmentId";
}

/// <summary>
/// Builds the template variable values for an appointment message. Values are
/// snapshot-friendly strings (no culture surprises): dates use the invariant
/// day/month order and times are 24-hour. Bundle appointments advertise the
/// bundle name instead of dumping each component service.
/// </summary>
public class ReminderMessageComposer
{
    private readonly string _salonName;
    private readonly TimeProvider _timeProvider;

    public ReminderMessageComposer(string salonName, TimeProvider timeProvider)
    {
        _salonName = salonName;
        _timeProvider = timeProvider;
    }

    public IReadOnlyDictionary<string, string> Compose(Appointment appointment, string customerName)
    {
        var staffNames = appointment.Services
            .Select(line => line.Staff)
            .Where(staff => staff is not null)
            .Select(staff => staff!.FirstName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var serviceNames = appointment.Services
            .Select(line => line.ServiceNameSnapshot)
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        return new Dictionary<string, string>
        {
            [ReminderTemplateVariables.CustomerName] = customerName,
            [ReminderTemplateVariables.SalonName] = _salonName,
            [ReminderTemplateVariables.AppointmentDate] = appointment.StartsAt
                .ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture),
            [ReminderTemplateVariables.AppointmentTime] = appointment.StartsAt
                .ToString("HH:mm", CultureInfo.InvariantCulture),
            [ReminderTemplateVariables.StaffName] = staffNames.Count > 0
                ? string.Join(", ", staffNames)
                : "our team",
            [ReminderTemplateVariables.ServiceName] = appointment.ServiceBundle?.Name
                ?? (serviceNames.Count > 0 ? string.Join(", ", serviceNames) : "your appointment"),
            [ReminderTemplateVariables.AppointmentId] = appointment.Id.ToString()
        };
    }

    /// <summary>Fixed sample values for the admin "send test message" action.</summary>
    public IReadOnlyDictionary<string, string> ComposeTest()
    {
        var now = _timeProvider.GetUtcNow();

        return new Dictionary<string, string>
        {
            [ReminderTemplateVariables.CustomerName] = "Test Message (ignore)",
            [ReminderTemplateVariables.SalonName] = _salonName,
            [ReminderTemplateVariables.AppointmentDate] = now
                .ToString("ddd, dd MMM yyyy", CultureInfo.InvariantCulture),
            [ReminderTemplateVariables.AppointmentTime] = now
                .AddHours(1)
                .ToString("HH:mm", CultureInfo.InvariantCulture),
            [ReminderTemplateVariables.StaffName] = "Test staff",
            [ReminderTemplateVariables.ServiceName] = "Test service",
            [ReminderTemplateVariables.AppointmentId] = "test"
        };
    }
}
