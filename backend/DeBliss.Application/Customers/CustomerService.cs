using System.Net.Mail;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.Customers.Dtos;
using DeBliss.Application.Customers.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;

namespace DeBliss.Application.Customers;

public sealed class CustomerService(
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider timeProvider) : ICustomerService
{
    /// <summary>Where this front-desk endpoint captures consent.</summary>
    private const string FrontDeskOptInSource = "Reception";

    public async Task<PagedResult<CustomerSummaryDto>> SearchAsync(
        CustomerQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var (items, totalCount) = await customers.SearchAsync(normalized, cancellationToken);

        return new PagedResult<CustomerSummaryDto>(
            [.. items.Select(MapSummary)],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    public async Task<CustomerDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(id, cancellationToken);

        return await MapAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto> CreateAsync(
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludeId: null, cancellationToken);

        var customer = new Customer
        {
            FirstName = validated.FirstName,
            LastName = validated.LastName,
            PhoneNumber = validated.PhoneNumber,
            Email = validated.Email,
            DateOfBirth = validated.DateOfBirth,
            Gender = validated.Gender,
            Address = validated.Address,
            IsActive = request.IsActive ?? true
        };

        customers.Add(customer);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MapAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto> UpdateAsync(
        Guid id,
        SaveCustomerRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = await ValidateAsync(request, excludeId: id, cancellationToken);

        var customer = await LoadAsync(id, cancellationToken);

        customer.FirstName = validated.FirstName;
        customer.LastName = validated.LastName;
        customer.PhoneNumber = validated.PhoneNumber;
        customer.Email = validated.Email;
        customer.DateOfBirth = validated.DateOfBirth;
        customer.Gender = validated.Gender;
        customer.Address = validated.Address;

        if (request.IsActive is not null)
        {
            customer.IsActive = request.IsActive.Value;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MapAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var customer = await LoadAsync(id, cancellationToken);

        customer.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MapAsync(customer, cancellationToken);
    }

    public async Task<CustomerDto> SetWhatsAppOptInAsync(
        Guid id,
        string? status,
        CancellationToken cancellationToken = default)
    {
        var errors = new ValidationErrorBuilder();

        WhatsAppOptInStatus? parsed = null;

        if (string.IsNullOrWhiteSpace(status))
        {
            errors.Add("status", "Choose a consent state.");
        }
        else if (!Enum.TryParse<WhatsAppOptInStatus>(status.Trim(), ignoreCase: true, out var value))
        {
            errors.Add("status", $"'{status}' is not a recognised consent state.");
        }
        else
        {
            parsed = value;
        }

        errors.ThrowIfInvalid();

        var customer = await LoadAsync(id, cancellationToken);

        switch (parsed!.Value)
        {
            case WhatsAppOptInStatus.OptedIn:
                customer.WhatsAppOptInStatus = WhatsAppOptInStatus.OptedIn;
                customer.WhatsAppOptInDate = timeProvider.GetUtcNow();
                customer.WhatsAppOptInSource = FrontDeskOptInSource;
                customer.WhatsAppOptOutDate = null;
                break;
            case WhatsAppOptInStatus.OptedOut:
                customer.WhatsAppOptInStatus = WhatsAppOptInStatus.OptedOut;
                customer.WhatsAppOptOutDate = timeProvider.GetUtcNow();
                break;
            case WhatsAppOptInStatus.Unknown:
                customer.WhatsAppOptInStatus = WhatsAppOptInStatus.Unknown;
                customer.WhatsAppOptInDate = null;
                customer.WhatsAppOptOutDate = null;
                customer.WhatsAppOptInSource = null;
                break;
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return await MapAsync(customer, cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerNoteDto>> ListNotesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(id, cancellationToken);

        var notes = await customers.ListNotesAsync(id, cancellationToken);

        return [.. notes.Select(MapNote)];
    }

    public async Task<CustomerNoteDto> AddNoteAsync(
        Guid id,
        SaveCustomerNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var noteText = ValidateNote(request);

        await LoadAsync(id, cancellationToken);

        var note = new CustomerNote
        {
            CustomerId = id,
            Note = noteText
        };

        customers.AddNote(note);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapNote(note);
    }

    public async Task<CustomerNoteDto> UpdateNoteAsync(
        Guid id,
        Guid noteId,
        SaveCustomerNoteRequest request,
        CancellationToken cancellationToken = default)
    {
        var noteText = ValidateNote(request);

        await LoadAsync(id, cancellationToken);

        var note = await customers.GetNoteAsync(id, noteId, cancellationToken)
            ?? throw new NotFoundException($"Note '{noteId}' was not found.");

        note.Note = noteText;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapNote(note);
    }

    public async Task<IReadOnlyList<CustomerAppointmentDto>> ListAppointmentsAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        await LoadAsync(id, cancellationToken);

        var appointments = await customers.ListAppointmentsAsync(id, cancellationToken);

        return [.. appointments.Select(MapAppointment)];
    }

    private async Task<Customer> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await customers.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Customer '{id}' was not found.");
    }

    private async Task<ValidatedCustomer> ValidateAsync(
        SaveCustomerRequest request,
        Guid? excludeId,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var firstName = request.FirstName?.Trim() ?? string.Empty;

        if (firstName.Length == 0)
        {
            errors.Add("firstName", "First name is required.");
        }
        else if (firstName.Length > CustomerLimits.NameMaxLength)
        {
            errors.Add("firstName", $"First name must be {CustomerLimits.NameMaxLength} characters or fewer.");
        }

        var lastName = request.LastName?.Trim() ?? string.Empty;

        if (lastName.Length == 0)
        {
            errors.Add("lastName", "Last name is required.");
        }
        else if (lastName.Length > CustomerLimits.NameMaxLength)
        {
            errors.Add("lastName", $"Last name must be {CustomerLimits.NameMaxLength} characters or fewer.");
        }

        var phoneNumber = request.PhoneNumber?.Trim() ?? string.Empty;

        if (phoneNumber.Length == 0)
        {
            errors.Add("phoneNumber", "Mobile number is required.");
        }
        else if (phoneNumber.Length > CustomerLimits.PhoneMaxLength)
        {
            errors.Add("phoneNumber", $"Mobile number must be {CustomerLimits.PhoneMaxLength} characters or fewer.");
        }
        else
        {
            var digits = CustomerPhone.Normalize(phoneNumber);

            if (digits is null || digits.Length < CustomerLimits.MinNormalizedPhoneDigits)
            {
                errors.Add("phoneNumber", "Enter a valid mobile number.");
            }
        }

        var email = NormalizeOptional(request.Email);

        if (request.Email?.Length > CustomerLimits.EmailMaxLength)
        {
            errors.Add("email", $"Email must be {CustomerLimits.EmailMaxLength} characters or fewer.");
        }
        else if (email is not null && !IsValidEmail(email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        if (request.DateOfBirth is { } dateOfBirth && dateOfBirth > DateOnly.FromDateTime(DateTime.UtcNow))
        {
            errors.Add("dateOfBirth", "Date of birth cannot be in the future.");
        }

        var gender = NormalizeOptional(request.Gender);

        if (request.Gender?.Length > CustomerLimits.GenderMaxLength)
        {
            errors.Add("gender", $"Gender must be {CustomerLimits.GenderMaxLength} characters or fewer.");
        }

        var address = NormalizeOptional(request.Address);

        if (request.Address?.Length > CustomerLimits.AddressMaxLength)
        {
            errors.Add("address", $"Address must be {CustomerLimits.AddressMaxLength} characters or fewer.");
        }

        errors.ThrowIfInvalid();

        if (CustomerPhone.Normalize(phoneNumber) is { } normalizedPhone && !request.AllowDuplicatePhone)
        {
            var matches = await customers.GetByNormalizedPhoneAsync(normalizedPhone, excludeId, cancellationToken);
            var existing = matches.FirstOrDefault();

            if (existing is not null)
            {
                throw new ConflictException(
                    $"{existing.FirstName} {existing.LastName} already uses this mobile number. " +
                    "A second record under the same number can split the appointment history between two profiles. " +
                    "Create the record anyway only if you are sure this is a different person.");
            }
        }

        return new ValidatedCustomer(firstName, lastName, phoneNumber, email, request.DateOfBirth, gender, address);
    }

    private static string ValidateNote(SaveCustomerNoteRequest request)
    {
        var note = request.Note?.Trim() ?? string.Empty;

        if (note.Length == 0)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["note"] = ["A note cannot be empty."]
            });
        }

        if (request.Note?.Length > CustomerLimits.NoteMaxLength)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["note"] = [$"Note must be {CustomerLimits.NoteMaxLength} characters or fewer."]
            });
        }

        return note;
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);

            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static string? NormalizeOptional(string? value)
    {
        var trimmed = value?.Trim();

        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    private async Task<CustomerDto> MapAsync(Customer customer, CancellationToken cancellationToken)
    {
        var summary = await customers.GetAppointmentSummaryAsync(customer.Id, cancellationToken);

        return new CustomerDto(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.PhoneNumber,
            customer.DateOfBirth,
            customer.Gender,
            customer.Address,
            customer.IsActive,
            customer.WhatsAppOptInStatus.ToString(),
            customer.WhatsAppOptInDate,
            customer.WhatsAppOptOutDate,
            customer.WhatsAppOptInSource,
            new CustomerAppointmentSummaryDto(
                summary.Total,
                summary.Completed,
                summary.Cancelled,
                summary.NoShow,
                summary.LastVisitAt),
            customer.CreatedAt,
            customer.UpdatedAt);
    }

    private static CustomerSummaryDto MapSummary(Customer customer) => new(
        customer.Id,
        customer.FirstName,
        customer.LastName,
        customer.PhoneNumber,
        customer.Email,
        customer.IsActive,
        customer.CreatedAt,
        customer.UpdatedAt);

    private static CustomerNoteDto MapNote(CustomerNote note) => new(
        note.Id,
        note.CustomerId,
        note.Note,
        note.CreatedAt,
        note.UpdatedAt);

    private static CustomerAppointmentDto MapAppointment(Appointment appointment) => new(
        appointment.Id,
        appointment.StartsAt,
        appointment.EndsAt,
        appointment.Status.ToString(),
        [.. appointment.Services
            .OrderBy(line => line.SortOrder)
            .Select(line => line.ServiceNameSnapshot)
            .Distinct(StringComparer.Ordinal)],
        [.. appointment.Services
            .Where(line => line.Staff is not null)
            .Select(line => $"{line.Staff!.FirstName} {line.Staff!.LastName}")
            .Distinct(StringComparer.Ordinal)]);

    private sealed record ValidatedCustomer(
        string FirstName,
        string LastName,
        string PhoneNumber,
        string? Email,
        DateOnly? DateOfBirth,
        string? Gender,
        string? Address);
}
