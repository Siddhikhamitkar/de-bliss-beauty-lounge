using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Scheduling;

/// <summary>
/// The single projection from a loaded Appointment graph to AppointmentDto.
/// AppointmentService and CalendarService must render a booking identically,
/// so the mapping lives in one place.
/// </summary>
internal static class AppointmentMapping
{
    public static AppointmentDto Map(Appointment appointment)
    {
        var customerName = appointment.Customer is { } customer
            ? $"{customer.FirstName} {customer.LastName}"
            : appointment.WalkInCustomerName ?? "Walk-in";

        var customerPhone = appointment.Customer?.PhoneNumber ?? appointment.WalkInCustomerPhone;

        var lines = appointment.Services
            .OrderBy(line => line.SortOrder)
            .Select(line => new AppointmentServiceLineDto(
                line.ServiceId,
                line.ServiceNameSnapshot,
                line.DurationMinutes,
                line.UnitPrice,
                line.StaffId ?? Guid.Empty,
                line.Staff?.FirstName ?? string.Empty,
                line.Staff?.LastName ?? string.Empty,
                line.Staff?.CalendarColor))
            .ToList();

        return new AppointmentDto(
            appointment.Id,
            appointment.CustomerId,
            customerName,
            customerPhone,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.Status.ToString(),
            appointment.Source.ToString(),
            appointment.Notes,
            appointment.CancellationReason,
            lines,
            lines.Sum(line => line.UnitPrice),
            lines.Sum(line => line.DurationMinutes),
            appointment.CreatedAt,
            appointment.UpdatedAt,
            appointment.ServiceBundleId,
            appointment.ServiceBundle?.Name,
            appointment.BundlePriceSnapshot);
    }
}
