using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Scheduling.Dtos;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Scheduling;
using System.Security.Cryptography;
using System.Text;
namespace DeBliss.Application.Scheduling;

public interface ICustomerBookingService
{
    /// <summary>
    /// Books one service for the signed-in customer. The idempotency key makes
    /// the operation replay-safe; "any available" (no staff preference) is
    /// resolved by the server from the same eligibility rules that would
    /// validate an explicit choice.
    /// </summary>
    Task<CustomerBookingConfirmationDto> BookAsync(
        Guid customerId,
        CustomerBookingRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default);

    /// <summary>The customer's own appointments, newest first.</summary>
    Task<IReadOnlyList<CustomerAppointmentDto>> GetMyAppointmentsAsync(Guid customerId, CancellationToken cancellationToken = default);
}

/// <summary>
/// The customer booking use case. Identity arrives from the token's customer
/// claim, never from the payload. Everything the front desk validates before
/// booking is revalidated here through the very same
/// <see cref="IAppointmentService.CreateAsync"/> path — active service, staff
/// assignment, snapshots, the availability gate — so an online booking can
/// never succeed where a reception booking would have been refused. The whole
/// sequence runs inside one serializable transaction: the idempotency claim is
/// written first (taking the write lock), availability is re-established
/// inside that transaction, and claim, appointment and reminders commit
/// together — so two simultaneous bookings for the same staff member serialize
/// and the loser is told the slot is gone.
/// </summary>
public sealed class CustomerBookingService(
    IAppointmentService appointments,
    IStaffAvailabilityService availability,
    IStaffRepository staff,
    IServiceRepository services,
    ICustomerRepository customers,
    IBookingClaimRepository claims,
    IUnitOfWork unitOfWork) : ICustomerBookingService
{
    public const int IdempotencyKeyMaxLength = 100;

    public async Task<CustomerBookingConfirmationDto> BookAsync(
        Guid customerId,
        CustomerBookingRequest request,
        string? idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        var key = idempotencyKey?.Trim();

        if (string.IsNullOrEmpty(key))
        {
            errors.Add("idempotencyKey", "Include an Idempotency-Key header so a repeated request cannot double-book.");
        }
        else if (key.Length > IdempotencyKeyMaxLength)
        {
            errors.Add("idempotencyKey", $"The idempotency key must be {IdempotencyKeyMaxLength} characters or fewer.");
        }

        if (request.ServiceId == Guid.Empty)
        {
            errors.Add("serviceId", "Choose a service.");
        }

        if (request.StartsAt is null)
        {
            errors.Add("startsAt", "Choose a start time.");
        }

        errors.ThrowIfInvalid();

        var service = await services.GetByIdAsync(request.ServiceId, cancellationToken)
            ?? throw new NotFoundException($"Service '{request.ServiceId}' was not found.");

        if (!service.IsActive || service.ServiceCategory is { IsActive: false } || !service.IsOnlineBookable)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["serviceId"] = [$"'{service.Name}' is not available for online booking."]
            });
        }

        var start = request.StartsAt!.Value;
        var staffId = await ResolveStaffAsync(service.Id, request.StaffId, start, service.DurationMinutes, cancellationToken);
        var requestHash = HashRequest(customerId, service.Id, staffId, start);

        await using var transaction = await unitOfWork.BeginTransactionAsync(cancellationToken);

        if (await claims.FindAsync(customerId, key!, cancellationToken) is { } existing)
        {
            return await ReplayAsync(existing, requestHash, cancellationToken);
        }

        // Inside the serializable transaction this either takes the claim or
        // loses to a concurrent identical-key request, whose outcome is then
        // replayed — never a second booking.
        if (await claims.TryAddAsync(new BookingClaim
            {
                Id = Guid.NewGuid(),
                CustomerId = customerId,
                IdempotencyKey = key!,
                RequestHash = requestHash,
            }, cancellationToken) is { } winner)
        {
            return await ReplayAsync(winner, requestHash, cancellationToken);
        }

        var booked = await appointments.CreateAsync(
            new SaveAppointmentRequest(
                customerId,
                WalkInCustomerName: null,
                WalkInCustomerPhone: null,
                start,
                [new AppointmentServiceLineInput(service.Id, staffId)],
                Notes: null),
            cancellationToken,
            AppointmentSource.Online);

        await claims.LinkAppointmentAsync(customerId, key!, booked.Id, cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return MapConfirmation(booked);
    }

    public async Task<IReadOnlyList<CustomerAppointmentDto>> GetMyAppointmentsAsync(
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var history = await customers.ListAppointmentsAsync(customerId, cancellationToken);

        return [.. history.Select(MapHistory)];
    }

    /// <summary>
    /// An explicit preference must name a real member — assignment, working
    /// hours and the availability gate are enforced downstream by the shared
    /// booking validation. With no preference, the server picks the first
    /// member the existing engine says is genuinely available for the window.
    /// </summary>
    private async Task<Guid> ResolveStaffAsync(
        Guid serviceId,
        Guid? requestedStaffId,
        DateTimeOffset start,
        int durationMinutes,
        CancellationToken cancellationToken)
    {
        if (requestedStaffId is { } requested)
        {
            _ = await staff.GetByIdAsync(requested, cancellationToken)
                ?? throw new NotFoundException($"Staff member '{requested}' was not found.");

            return requested;
        }

        var end = start.AddMinutes(durationMinutes);
        var candidates = await availability.GetAvailableStaffAsync(serviceId, start, end, cancellationToken);

        return candidates.FirstOrDefault()?.Id
            ?? throw new ConflictException("No staff member is available at the selected time.");
    }

    private async Task<CustomerBookingConfirmationDto> ReplayAsync(
        BookingClaim claim,
        string requestHash,
        CancellationToken cancellationToken)
    {
        if (claim.RequestHash != requestHash)
        {
            throw new ConflictException(
                "This idempotency key was already used for a different booking request. Use a fresh key.");
        }

        if (claim.AppointmentId is not { } appointmentId)
        {
            throw new ConflictException("This idempotency key is still being processed. Retry shortly.");
        }

        var appointment = await appointments.GetAsync(appointmentId, cancellationToken);

        return MapConfirmation(appointment);
    }

    private static string HashRequest(Guid customerId, Guid serviceId, Guid staffId, DateTimeOffset start) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{customerId}|{serviceId}|{staffId}|{start.UtcTicks}")));

    private static CustomerBookingConfirmationDto MapConfirmation(AppointmentDto appointment)
    {
        var line = appointment.Services[0];

        return new CustomerBookingConfirmationDto(
            appointment.Id,
            line.ServiceName,
            appointment.StartsAt,
            appointment.EndsAt,
            appointment.Status,
            DisplayName(line.StaffFirstName, line.StaffLastName),
            line.UnitPrice,
            appointment.Source);
    }

    private static CustomerAppointmentDto MapHistory(Domain.Scheduling.Appointment appointment) => new(
        appointment.Id,
        appointment.StartsAt,
        appointment.EndsAt,
        appointment.Status.ToString(),
        [.. appointment.Services
            .OrderBy(line => line.SortOrder)
            .Select(line => new CustomerAppointmentServiceDto(
                line.ServiceNameSnapshot,
                line.DurationMinutes,
                line.UnitPrice,
                line.Staff is null ? null : DisplayName(line.Staff.FirstName, line.Staff.LastName)))],
        appointment.Services.Sum(line => line.UnitPrice),
        appointment.Services.Sum(line => line.DurationMinutes));

    private static string DisplayName(string firstName, string lastName)
    {
        var name = $"{firstName} {lastName}".Trim();

        return name.Length == 0 ? "Your therapist" : name;
    }
}
