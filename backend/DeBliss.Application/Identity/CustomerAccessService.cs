using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Application.Identity.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace DeBliss.Application.Identity;

/// <summary>
/// Customer self-service identity. Registration hashes with the same
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2) the login path verifies against,
/// assigns only the CUSTOMER role, and commits the User, its role assignment and
/// the linked-or-new Customer record as one save — either all three rows exist
/// afterwards or none do.
/// </summary>
public sealed class CustomerAccessService(
    IUserRepository users,
    ICustomerRepository customers,
    IUnitOfWork unitOfWork,
    TimeProvider clock) : ICustomerAccessService
{
    public async Task<AuthUserDto> RegisterAsync(
        CustomerRegistrationRequest request,
        CancellationToken cancellationToken)
    {
        var errors = new ValidationErrorBuilder();

        var firstName = request.FirstName?.Trim() ?? string.Empty;
        if (firstName.Length == 0)
        {
            errors.Add("firstName", "First name is required.");
        }

        var lastName = request.LastName?.Trim() ?? string.Empty;
        if (lastName.Length == 0)
        {
            errors.Add("lastName", "Last name is required.");
        }

        var phoneNumber = request.PhoneNumber?.Trim();
        if (!string.IsNullOrEmpty(phoneNumber))
        {
            if (phoneNumber.Length > CustomerLimits.PhoneMaxLength)
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
        }

        errors.ThrowIfInvalid();

        var email = request.Email.Trim();

        if (await users.FindByEmailAsync(email, cancellationToken) is not null)
        {
            throw new ConflictException("An account with this email already exists.");
        }

        var roleId = await users.GetRoleIdAsync(RoleNames.Customer, cancellationToken)
            ?? throw new InvalidOperationException(
                $"The '{RoleNames.Customer}' role is not present in the database. " +
                "Seed the platform roles before enabling public registration.");

        var now = clock.GetUtcNow();

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = string.IsNullOrEmpty(phoneNumber) ? null : phoneNumber,
            PasswordHash = AuthService.HashPassword(request.Password),
            IsActive = true,
        };
        user.UserRoles.Add(new UserRole { RoleId = roleId, AssignedAt = now });

        var customer = await customers.FindLinkableByEmailAsync(email, cancellationToken);

        if (customer is { } existing)
        {
            // An unambiguous front-desk record for this person already exists:
            // adopt it instead of splitting their history across two profiles.
            // Front-desk data is never overwritten — only the link is set.
            existing.UserId = user.Id;
        }
        else
        {
            customer = new Customer
            {
                Id = Guid.NewGuid(),
                UserId = user.Id,
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                PhoneNumber = user.PhoneNumber,
                IsWalkIn = false,
            };
            customers.Add(customer);
        }

        users.Add(user);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return new AuthUserDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            [RoleNames.Customer],
            StaffId: null,
            CustomerId: customer.Id);
    }

    public async Task<CustomerSelfDto> GetProfileAsync(Guid customerId, CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{customerId}' was not found.");

        return new CustomerSelfDto(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.PhoneNumber);
    }
}
