using System.Net.Mail;
using System.Text.RegularExpressions;
using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Common.Paging;
using DeBliss.Application.ServiceCatalogue.Dtos;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing.Dtos;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Domain.Staffing;
using StaffAssignment = DeBliss.Domain.Staffing.StaffService;

namespace DeBliss.Application.Staffing;

public sealed class StaffService(
    IStaffRepository staff,
    IServiceRepository services,
    IUnitOfWork unitOfWork) : IStaffService
{
    private static readonly Regex ColorPattern = new(
        "^#([0-9A-Fa-f]{3}|[0-9A-Fa-f]{6})$",
        RegexOptions.None,
        TimeSpan.FromMilliseconds(100));

    public async Task<PagedResult<StaffSummaryDto>> SearchAsync(
        StaffQuery query,
        CancellationToken cancellationToken = default)
    {
        var normalized = query.Normalized();

        var (items, totalCount) = await staff.SearchAsync(normalized, cancellationToken);

        return new PagedResult<StaffSummaryDto>(
            [.. items.Select(MapSummary)],
            totalCount,
            normalized.Page,
            normalized.PageSize);
    }

    public async Task<StaffDto> GetAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(id, cancellationToken);

        return Map(member);
    }

    public async Task<StaffDto> CreateAsync(
        SaveStaffRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = Validate(request);

        var member = new Staff
        {
            FirstName = validated.FirstName,
            LastName = validated.LastName,
            Email = validated.Email,
            PhoneNumber = validated.PhoneNumber,
            JobTitle = validated.JobTitle,
            CalendarColor = validated.CalendarColor,
            IsActive = request.IsActive,
            SortOrder = request.SortOrder
        };

        ApplySalary(member, validated.Salary);
        staff.Add(member);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(member);
    }

    public async Task<StaffDto> UpdateAsync(
        Guid id,
        SaveStaffRequest request,
        CancellationToken cancellationToken = default)
    {
        var validated = Validate(request);

        var member = await LoadAsync(id, cancellationToken);

        member.FirstName = validated.FirstName;
        member.LastName = validated.LastName;
        member.Email = validated.Email;
        member.PhoneNumber = validated.PhoneNumber;
        member.JobTitle = validated.JobTitle;
        member.CalendarColor = validated.CalendarColor;
        member.IsActive = request.IsActive;
        member.SortOrder = request.SortOrder;
        ApplySalary(member, validated.Salary);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(member);
    }

    public async Task<StaffDto> SetStatusAsync(
        Guid id,
        bool isActive,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(id, cancellationToken);

        member.IsActive = isActive;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Map(member);
    }

    public async Task<IReadOnlyList<StaffServiceDto>> GetServicesAsync(
        Guid id,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(id, cancellationToken);

        return MapServices(member);
    }

    public async Task<IReadOnlyList<StaffServiceDto>> SetServicesAsync(
        Guid id,
        SetStaffServicesRequest request,
        CancellationToken cancellationToken = default)
    {
        var member = await LoadAsync(id, cancellationToken);

        var serviceIds = NormalizeServiceIds(request.ServiceIds);

        var found = serviceIds.Count == 0
            ? []
            : await services.GetByIdsAsync(serviceIds, cancellationToken);

        var foundIds = found.Select(service => service.Id).ToHashSet();
        var unknownCount = serviceIds.Count(serviceId => !foundIds.Contains(serviceId));

        if (unknownCount > 0)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>(StringComparer.Ordinal)
            {
                ["serviceIds"] =
                [
                    $"{unknownCount} of the selected services no longer exist. Remove them and try again."
                ]
            });
        }

        var wanted = serviceIds.ToHashSet();

        foreach (var line in member.StaffServices.Where(line => !wanted.Contains(line.ServiceId)).ToList())
        {
            member.StaffServices.Remove(line);
        }

        foreach (var service in found.Where(service => !member.StaffServices.Any(line => line.ServiceId == service.Id)))
        {
            member.StaffServices.Add(new StaffAssignment
            {
                Service = service,
                Staff = member
            });
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return MapServices(member);
    }

    private async Task<Staff> LoadAsync(Guid id, CancellationToken cancellationToken)
    {
        return await staff.GetByIdAsync(id, cancellationToken)
            ?? throw new NotFoundException($"Staff member '{id}' was not found.");
    }

    private static ValidatedStaff Validate(SaveStaffRequest request)
    {
        var errors = new ValidationErrorBuilder();

        var firstName = request.FirstName?.Trim() ?? string.Empty;

        if (firstName.Length == 0)
        {
            errors.Add("firstName", "First name is required.");
        }
        else if (firstName.Length > StaffingLimits.NameMaxLength)
        {
            errors.Add("firstName", $"First name must be {StaffingLimits.NameMaxLength} characters or fewer.");
        }

        var lastName = request.LastName?.Trim() ?? string.Empty;

        if (lastName.Length == 0)
        {
            errors.Add("lastName", "Last name is required.");
        }
        else if (lastName.Length > StaffingLimits.NameMaxLength)
        {
            errors.Add("lastName", $"Last name must be {StaffingLimits.NameMaxLength} characters or fewer.");
        }

        var email = NormalizeOptional(request.Email);

        if (request.Email?.Length > StaffingLimits.EmailMaxLength)
        {
            errors.Add("email", $"Email must be {StaffingLimits.EmailMaxLength} characters or fewer.");
        }
        else if (email is not null && !IsValidEmail(email))
        {
            errors.Add("email", "Enter a valid email address.");
        }

        var phoneNumber = NormalizeOptional(request.PhoneNumber);

        if (request.PhoneNumber?.Length > StaffingLimits.PhoneMaxLength)
        {
            errors.Add("phoneNumber", $"Phone number must be {StaffingLimits.PhoneMaxLength} characters or fewer.");
        }

        var jobTitle = NormalizeOptional(request.JobTitle);

        if (request.JobTitle?.Length > StaffingLimits.JobTitleMaxLength)
        {
            errors.Add("jobTitle", $"Job title must be {StaffingLimits.JobTitleMaxLength} characters or fewer.");
        }

        var calendarColor = NormalizeOptional(request.CalendarColor);

        if (calendarColor is not null && !ColorPattern.IsMatch(calendarColor))
        {
            errors.Add("calendarColor", "Use a colour in the form #RGB or #RRGGBB, for example #E91E63.");
        }

        if (request.SortOrder < 0)
        {
            errors.Add("sortOrder", "Display position cannot be negative.");
        }

        var salary = ValidateSalary(request.Salary, errors);

        errors.ThrowIfInvalid();

        return new ValidatedStaff(firstName, lastName, email, phoneNumber, jobTitle, calendarColor, salary);
    }

    private static StaffSalaryDetails? ValidateSalary(StaffSalaryDetails? salary, ValidationErrorBuilder errors)
    {
        if (salary is null)
        {
            return null;
        }

        ValidateSalaryAmount(salary.TotalSalary, "salary.totalSalary", errors);
        ValidateSalaryAmount(salary.BasicSalary, "salary.basicSalary", errors);
        ValidateSalaryAmount(salary.OtherAllowances, "salary.otherAllowances", errors);

        var normalized = salary with
        {
            IbanNumber = NormalizeOptional(salary.IbanNumber),
            BankName = NormalizeOptional(salary.BankName),
            BankAccountNumber = NormalizeOptional(salary.BankAccountNumber)
        };

        if (normalized.IbanNumber?.Length > 50)
        {
            errors.Add("salary.ibanNumber", "IBAN must be 50 characters or fewer.");
        }

        if (normalized.BankName?.Length > 200)
        {
            errors.Add("salary.bankName", "Bank name must be 200 characters or fewer.");
        }

        if (normalized.BankAccountNumber?.Length > 100)
        {
            errors.Add("salary.bankAccountNumber", "Bank account number must be 100 characters or fewer.");
        }

        return normalized;
    }

    private static void ValidateSalaryAmount(decimal? amount, string field, ValidationErrorBuilder errors)
    {
        if (amount is not { } value)
        {
            return;
        }

        if (value is < 0 or > 999999999.99m)
        {
            errors.Add(field, "Amount must be between 0 and 999999999.99.");
        }
        else if (decimal.Round(value, 2) != value)
        {
            errors.Add(field, "Amount must have at most two decimal places.");
        }
    }

    private static void ApplySalary(Staff member, StaffSalaryDetails? salary)
    {
        if (salary is null)
        {
            return;
        }

        member.TotalSalary = salary.TotalSalary;
        member.BasicSalary = salary.BasicSalary;
        member.OtherAllowances = salary.OtherAllowances;
        member.IbanNumber = salary.IbanNumber;
        member.BankName = salary.BankName;
        member.BankAccountNumber = salary.BankAccountNumber;
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

    private static List<Guid> NormalizeServiceIds(IReadOnlyList<Guid>? serviceIds)
    {
        if (serviceIds is null)
        {
            return [];
        }

        var seen = new HashSet<Guid>();
        var result = new List<Guid>(serviceIds.Count);

        foreach (var serviceId in serviceIds)
        {
            if (serviceId != Guid.Empty && seen.Add(serviceId))
            {
                result.Add(serviceId);
            }
        }

        return result;
    }

    private static StaffSummaryDto MapSummary(Staff member) => new(
        member.Id,
        member.FirstName,
        member.LastName,
        member.JobTitle,
        member.CalendarColor,
        member.IsActive,
        member.StaffServices.Count(line => line.Service.IsActive),
        member.SortOrder,
        member.CreatedAt,
        member.UpdatedAt);

    private static StaffDto Map(Staff member) => new(
        member.Id,
        member.FirstName,
        member.LastName,
        member.Email,
        member.PhoneNumber,
        member.JobTitle,
        member.CalendarColor,
        member.IsActive,
        member.SortOrder,
        MapServices(member),
        member.CreatedAt,
        member.UpdatedAt);

    private static IReadOnlyList<StaffServiceDto> MapServices(Staff member) =>
        [.. member.StaffServices
            .Select(line => line.Service)
            .OrderBy(service => service.Name, StringComparer.Ordinal)
            .Select(service => new StaffServiceDto(
                service.Id,
                service.Name,
                service.ServiceCategory?.Name,
                service.IsActive))];

    private sealed record ValidatedStaff(
        string FirstName,
        string LastName,
        string? Email,
        string? PhoneNumber,
        string? JobTitle,
        string? CalendarColor,
        StaffSalaryDetails? Salary);
}
