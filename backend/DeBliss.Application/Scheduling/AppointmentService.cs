using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Notifications;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using AppointmentLine = DeBliss.Domain.Scheduling.AppointmentService;

namespace DeBliss.Application.Scheduling;

/// <summary>
/// The booking use cases behind the calendar. Every rule that decides whether
/// a slot may be booked lives here: customer-or-walk-in resolution, snapshot
/// capture onto the service lines, the availability check per involved staff
/// member, and the status transition matrix with its history rows. Terminal
/// appointments (completed, cancelled, no-show) are historical record: they
/// can be read but never edited, moved or re-booked.
/// </summary>
public sealed class AppointmentService(
    IAppointmentRepository appointments,
    ICustomerRepository customers,
    IServiceRepository services,
    IServiceBundleRepository bundles,
    IStaffRepository staff,
    IStaffAvailabilityService availability,
    ReminderPlanner reminderPlanner,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : IAppointmentService
{
    private static readonly AppointmentStatus[] TerminalStatuses =
    [
        AppointmentStatus.Completed,
        AppointmentStatus.Cancelled,
        AppointmentStatus.NoShow
    ];

    private static readonly Dictionary<AppointmentStatus, AppointmentStatus[]> AllowedTransitions = new()
    {
        [AppointmentStatus.Pending] = [AppointmentStatus.Confirmed, AppointmentStatus.Cancelled],
        [AppointmentStatus.Confirmed] = [AppointmentStatus.CheckedIn, AppointmentStatus.Cancelled, AppointmentStatus.NoShow],
        [AppointmentStatus.CheckedIn] = [AppointmentStatus.InProgress, AppointmentStatus.Cancelled],
        [AppointmentStatus.InProgress] = [AppointmentStatus.Completed],
        [AppointmentStatus.Waitlisted] = [AppointmentStatus.Confirmed, AppointmentStatus.Cancelled],
        [AppointmentStatus.Completed] = [],
        [AppointmentStatus.Cancelled] = [],
        [AppointmentStatus.NoShow] = []
    };

    public async Task<AppointmentDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAsync(id, cancellationToken);

        return Map(appointment);
    }

    public async Task<AppointmentDto> CreateAsync(
        SaveAppointmentRequest request,
        CancellationToken cancellationToken = default,
        AppointmentSource source = AppointmentSource.Reception)
    {
        var (start, lines, notes, bundle) = await ValidateBookingAsync(request, excludeId: null, cancellationToken);

        var appointment = new Appointment
        {
            CustomerId = request.CustomerId,
            WalkInCustomerName = request.CustomerId is null ? request.WalkInCustomerName?.Trim() : null,
            WalkInCustomerPhone = request.CustomerId is null ? request.WalkInCustomerPhone?.Trim() : null,
            StartsAt = start,
            EndsAt = start.AddMinutes(lines.Sum(line => line.Service.DurationMinutes)),
            Status = AppointmentStatus.Confirmed,
            Source = source,
            Notes = notes,
            ServiceBundleId = bundle?.Id,
            BundlePriceSnapshot = bundle?.Price
        };

        AddLines(appointment, lines);

        appointment.StatusHistory.Add(new AppointmentStatusHistory
        {
            FromStatus = null,
            ToStatus = appointment.Status,
            ChangedAt = timeProvider.GetUtcNow()
        });

        appointments.Add(appointment);

        await reminderPlanner.PlanForBookingAsync(appointment, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(appointment);
    }

    public async Task<AppointmentDto> UpdateAsync(
        Guid id,
        SaveAppointmentRequest request,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAsync(id, cancellationToken);

        EnsureEditable(appointment);

        var previousStartsAt = appointment.StartsAt;
        var previousCustomerId = appointment.CustomerId;

        var (start, lines, notes, bundle) = await ValidateBookingAsync(request, excludeId: id, cancellationToken);

        appointment.CustomerId = request.CustomerId;
        appointment.WalkInCustomerName = request.CustomerId is null ? request.WalkInCustomerName?.Trim() : null;
        appointment.WalkInCustomerPhone = request.CustomerId is null ? request.WalkInCustomerPhone?.Trim() : null;
        appointment.StartsAt = start;
        appointment.EndsAt = start.AddMinutes(lines.Sum(line => line.Service.DurationMinutes));
        appointment.Notes = notes;
        appointment.ServiceBundleId = bundle?.Id;
        appointment.BundlePriceSnapshot = bundle?.Price;

        // A full update replaces the service lines wholesale — the same
        // replace-set convention the service and staff assignment forms use.
        appointment.Services.Clear();
        AddLines(appointment, lines);

        await reminderPlanner.ApplyChangeAsync(appointment, previousStartsAt, previousCustomerId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(appointment);
    }

    public async Task<AppointmentDto> UpdateScheduleAsync(
        Guid id,
        UpdateScheduleRequest request,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAsync(id, cancellationToken);

        EnsureEditable(appointment);

        var previousStartsAt = appointment.StartsAt;

        var errors = new ValidationErrorBuilder();

        if (request.StartsAt is null && request.StaffId is null && request.DurationMinutes is null)
        {
            errors.Add("startsAt", "Nothing to change.");
        }

        var start = request.StartsAt ?? appointment.StartsAt;

        if (request.StartsAt is not null && start < timeProvider.GetUtcNow())
        {
            errors.Add("startsAt", "Start time cannot be in the past.");
        }

        var totalMinutes = appointment.Services.Sum(line => line.DurationMinutes);

        if (request.DurationMinutes is { } requestedDuration)
        {
            if (requestedDuration < SchedulingLimits.MinimumDurationMinutes)
            {
                errors.Add("durationMinutes", $"Duration must be at least {SchedulingLimits.MinimumDurationMinutes} minutes.");
            }
            else
            {
                var lastLine = appointment.Services.OrderByDescending(line => line.SortOrder).First();
                var newLastDuration = lastLine.DurationMinutes + (requestedDuration - totalMinutes);

                if (newLastDuration < SchedulingLimits.MinimumDurationMinutes)
                {
                    errors.Add("durationMinutes", $"Duration must be at least {SchedulingLimits.MinimumDurationMinutes} minutes.");
                }
            }
        }

        errors.ThrowIfInvalid();

        Staff? reassigned = null;

        if (request.StaffId is { } staffId)
        {
            reassigned = await staff.GetByIdAsync(staffId, cancellationToken)
                ?? throw new NotFoundException($"Staff member '{staffId}' was not found.");

            var notAssigned = appointment.Services
                .Where(line => !reassigned.StaffServices.Any(link => link.ServiceId == line.ServiceId))
                .Select(line => line.ServiceNameSnapshot)
                .Distinct()
                .ToList();

            if (notAssigned.Count > 0)
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
                {
                    ["staffId"] =
                    [
                        $"{reassigned.FirstName} {reassigned.LastName} is not assigned to " +
                        $"{string.Join(", ", notAssigned)}."
                    ]
                });
            }
        }

        if (request.DurationMinutes is { } duration)
        {
            var lastLine = appointment.Services.OrderByDescending(line => line.SortOrder).First();
            lastLine.DurationMinutes += duration - totalMinutes;
            totalMinutes = duration;
        }

        if (reassigned is not null)
        {
            foreach (var line in appointment.Services)
            {
                line.StaffId = reassigned.Id;
                line.Staff = reassigned;
            }
        }

        var end = start.AddMinutes(totalMinutes);

        await EnsureAvailableAsync(appointment, start, end, cancellationToken);

        appointment.StartsAt = start;
        appointment.EndsAt = end;

        await reminderPlanner.ApplyChangeAsync(appointment, previousStartsAt, appointment.CustomerId, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(appointment);
    }

    public async Task<AppointmentDto> SetStatusAsync(
        Guid id,
        SetAppointmentStatusRequest request,
        CancellationToken cancellationToken = default)
    {
        var appointment = await LoadAsync(id, cancellationToken);

        var errors = new ValidationErrorBuilder();

        if (string.IsNullOrWhiteSpace(request.Status))
        {
            errors.Add("status", "Choose a status.");
        }
        else if (!Enum.TryParse<AppointmentStatus>(request.Status.Trim(), ignoreCase: true, out _))
        {
            errors.Add("status", $"'{request.Status}' is not a recognised appointment status.");
        }

        if (request.Reason?.Length > SchedulingLimits.CancellationReasonMaxLength)
        {
            errors.Add("reason", $"Reason must be {SchedulingLimits.CancellationReasonMaxLength} characters or fewer.");
        }

        errors.ThrowIfInvalid();

        var target = Enum.Parse<AppointmentStatus>(request.Status!.Trim(), ignoreCase: true);

        if (TerminalStatuses.Contains(appointment.Status))
        {
            throw new ConflictException(
                $"A {appointment.Status} appointment is part of the historical record and cannot change status.");
        }

        if (!AllowedTransitions[appointment.Status].Contains(target))
        {
            throw new ConflictException(
                $"An appointment cannot move from {appointment.Status} to {target}.");
        }

        var from = appointment.Status;
        appointment.Status = target;
        appointment.CancellationReason = target == AppointmentStatus.Cancelled
            ? request.Reason?.Trim()
            : appointment.CancellationReason;

        appointment.StatusHistory.Add(new AppointmentStatusHistory
        {
            FromStatus = from,
            ToStatus = target,
            Reason = request.Reason?.Trim(),
            ChangedAt = timeProvider.GetUtcNow()
        });

        await reminderPlanner.ApplyStatusChangeAsync(appointment, from, target, cancellationToken);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(appointment);
    }

    private async Task<Appointment> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await appointments.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Appointment '{id}' was not found.");
    }

    private static void EnsureEditable(Appointment appointment)
    {
        if (TerminalStatuses.Contains(appointment.Status))
        {
            throw new ConflictException(
                $"A {appointment.Status} appointment cannot be changed.");
        }
    }

    /// <summary>
    /// Validates a create or full update and returns the resolved start, the
    /// snapshot lines, the trimmed notes and the bundle when the booking is a
    /// bundle booking. Syntactic problems are collected into one 400; missing
    /// records are 404; bookable/assignment problems are a second 400 pass;
    /// availability is the 409 gate that only fully valid input reaches. A
    /// bundle booking must submit exactly the bundle's components, one line
    /// each; the lines are returned in the bundle's sequence, so the stored
    /// lines render in performing order.
    /// </summary>
    private async Task<(
        DateTimeOffset Start,
        IReadOnlyList<ValidatedLine> Lines,
        string? Notes,
        ServiceBundle? Bundle)> ValidateBookingAsync(
        SaveAppointmentRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        if (request.CustomerId is null && string.IsNullOrWhiteSpace(request.WalkInCustomerName))
        {
            errors.Add("customerId", "Choose a customer or enter a walk-in name.");
        }

        if (request.CustomerId is not null && !string.IsNullOrWhiteSpace(request.WalkInCustomerName))
        {
            errors.Add("customerId", "Choose either a customer or a walk-in name, not both.");
        }

        if (request.WalkInCustomerName?.Length > SchedulingLimits.WalkInNameMaxLength)
        {
            errors.Add("walkInCustomerName", $"Walk-in name must be {SchedulingLimits.WalkInNameMaxLength} characters or fewer.");
        }

        if (request.WalkInCustomerPhone?.Length > SchedulingLimits.WalkInPhoneMaxLength)
        {
            errors.Add("walkInCustomerPhone", $"Walk-in phone must be {SchedulingLimits.WalkInPhoneMaxLength} characters or fewer.");
        }

        if (request.StartsAt is null)
        {
            errors.Add("startsAt", "Start time is required.");
        }
        else if (request.StartsAt < timeProvider.GetUtcNow())
        {
            errors.Add("startsAt", "Start time cannot be in the past.");
        }

        if (request.Services is null || request.Services.Count == 0)
        {
            errors.Add("services", "At least one service is required.");
        }

        if (request.Notes?.Length > SchedulingLimits.NotesMaxLength)
        {
            errors.Add("notes", $"Notes must be {SchedulingLimits.NotesMaxLength} characters or fewer.");
        }

        errors.ThrowIfInvalid();

        if (request.CustomerId is { } customerId)
        {
            _ = await customers.GetByIdAsync(customerId, cancellationToken)
                ?? throw new NotFoundException($"Customer '{customerId}' was not found.");
        }

        ServiceBundle? bundle = null;

        if (request.ServiceBundleId is { } bundleId)
        {
            bundle = await bundles.GetByIdAsync(bundleId, cancellationToken)
                ?? throw new NotFoundException($"Service bundle '{bundleId}' was not found.");
        }

        var start = request.StartsAt!.Value;
        var lines = new List<ValidatedLine>();
        var semanticErrors = new ValidationErrorBuilder();

        foreach (var input in request.Services!)
        {
            var service = await services.GetByIdAsync(input.ServiceId, cancellationToken)
                ?? throw new NotFoundException($"Service '{input.ServiceId}' was not found.");

            var member = await staff.GetByIdAsync(input.StaffId, cancellationToken)
                ?? throw new NotFoundException($"Staff member '{input.StaffId}' was not found.");

            if (!service.IsActive || service.ServiceCategory is { IsActive: false })
            {
                semanticErrors.Add("services", $"'{service.Name}' is no longer bookable.");
            }
            else if (!member.StaffServices.Any(link => link.ServiceId == service.Id))
            {
                semanticErrors.Add(
                    "services",
                    $"{member.FirstName} {member.LastName} is not assigned to '{service.Name}'.");
            }

            lines.Add(new ValidatedLine(service, member));
        }

        if (bundle is not null)
        {
            if (!bundle.CanBeBooked())
            {
                semanticErrors.Add("serviceBundleId", $"'{bundle.Name}' is no longer bookable.");
            }

            var componentIds = bundle.Items.Select(item => item.ServiceId).ToList();
            var requestedIds = lines.Select(line => line.Service.Id).ToList();

            if (requestedIds.Distinct().Count() != requestedIds.Count)
            {
                semanticErrors.Add("services", "Each service of a bundle may only be booked once.");
            }
            else if (requestedIds.Count != componentIds.Count || componentIds.Except(requestedIds).Any())
            {
                var names = string.Join(", ", bundle.Items
                    .OrderBy(item => item.DisplayOrder)
                    .Select(item => item.Service.Name));
                semanticErrors.Add(
                    "services",
                    $"Bundle '{bundle.Name}' contains: {names}. Submit exactly those services.");
            }
        }

        semanticErrors.ThrowIfInvalid();

        if (bundle is not null)
        {
            lines = bundle.Items
                .OrderBy(item => item.DisplayOrder)
                .Select(item => lines.First(line => line.Service.Id == item.ServiceId))
                .ToList();
        }

        var end = start.AddMinutes(lines.Sum(line => line.Service.DurationMinutes));

        await EnsureAvailableAsync(excludeId, lines, start, end, cancellationToken);

        return (start, lines, request.Notes?.Trim(), bundle);
    }

    /// <summary>The availability gate: every distinct staff member on the lines must be free for the whole window.</summary>
    private async Task EnsureAvailableAsync(
        Guid? excludeId,
        IReadOnlyList<ValidatedLine> lines,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        foreach (var member in lines.Select(line => line.Staff).DistinctBy(member => member.Id))
        {
            var check = await availability.CheckAsync(member.Id, start, end, excludeId, cancellationToken);

            if (!check.IsAvailable)
            {
                throw new ConflictException(
                    $"{member.FirstName} {member.LastName} is not available at the selected time.");
            }
        }
    }

    /// <summary>Availability check for the schedule-update path, where the lines are already on the appointment.</summary>
    private async Task EnsureAvailableAsync(
        Appointment appointment,
        DateTimeOffset start,
        DateTimeOffset end,
        CancellationToken cancellationToken)
    {
        foreach (var staffId in appointment.Services.Select(line => line.StaffId).Distinct())
        {
            if (staffId is null)
            {
                continue;
            }

            var check = await availability.CheckAsync(staffId.Value, start, end, appointment.Id, cancellationToken);

            if (!check.IsAvailable)
            {
                var member = appointment.Services.First(line => line.StaffId == staffId).Staff;

                throw new ConflictException(
                    $"{member?.FirstName} {member?.LastName ?? "The selected staff member"} is not available at the selected time.");
            }
        }
    }

    private static void AddLines(Appointment appointment, IReadOnlyList<ValidatedLine> lines)
    {
        var sortOrder = 0;

        foreach (var line in lines)
        {
            appointment.Services.Add(new AppointmentLine
            {
                ServiceId = line.Service.Id,
                StaffId = line.Staff.Id,
                Staff = line.Staff,
                ServiceNameSnapshot = line.Service.Name,
                DurationMinutes = line.Service.DurationMinutes,
                UnitPrice = line.Service.Price,
                SortOrder = sortOrder++
            });
        }
    }

    private static AppointmentDto Map(Appointment appointment) => AppointmentMapping.Map(appointment);

    private sealed record ValidatedLine(Service Service, Staff Staff);
}
