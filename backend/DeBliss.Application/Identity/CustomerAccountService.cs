using DeBliss.Application.Common.Exceptions;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Application.Identity.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DeBliss.Application.Identity;

/// <summary>
/// Proves mobile-number ownership with one-time codes and turns that proof into
/// account changes. Rules that hold throughout:
/// <list type="bullet">
/// <item>Unauthenticated answers never depend on whether a number is on file:
/// a code request gets the same response (and the same cooldown) either way,
/// and a wrong code gets the same message as an unknown number.</item>
/// <item>A code is single-purpose, expires, allows a few guesses and dies when a
/// newer code is sent; proving it yields a short-lived single-use token.</item>
/// <item>Only hashes of codes, tokens and passwords are stored.</item>
/// </list>
/// </summary>
public sealed class CustomerAccountService(
    ICustomerRepository customers,
    IUserRepository users,
    ICustomerVerificationCodeRepository codes,
    ICustomerOtpSender sender,
    IUnitOfWork unitOfWork,
    IOptions<CustomerOtpOptions> options,
    TimeProvider clock,
    ILogger<CustomerAccountService> logger) : ICustomerAccountService
{
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 200;

    private const string InvalidCodeMessage = "The code is incorrect or has expired. Request a new code and try again.";
    private const string InvalidTokenMessage = "This verification has expired. Start again and request a new code.";

    private CustomerOtpOptions Policy => options.Value;

    public async Task RequestCodeAsync(RequestCustomerCodeRequest request, CancellationToken cancellationToken)
    {
        var phone = RequirePhone(request.PhoneNumber);
        var purpose = RequirePublicPurpose(request.Purpose);

        await EnforceSendLimitsAsync(phone, purpose, cancellationToken);

        var customer = await customers.FindByCanonicalPhoneAsync(phone, cancellationToken);

        // Activation always sends: it is also how a brand-new customer signs
        // up, so a number with no record is a normal case. Whether the account
        // is already active is only revealed after the code is proven.
        // A reset only sends to a real account; for any other number a decoy
        // row is stored so the cooldown behaves identically.
        var shouldSend = purpose switch
        {
            CustomerVerificationPurpose.Activation => true,
            CustomerVerificationPurpose.PasswordReset => customer?.UserId is not null,
            _ => false
        };

        var code = VerificationSecrets.NewCode(Policy.CodeLength);
        Store(phone, purpose, customer?.Id, code);

        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (shouldSend)
        {
            await SendAsync(phone, code, purpose, cancellationToken);
        }
    }

    public async Task<CustomerCodeVerifiedDto> VerifyCodeAsync(
        VerifyCustomerCodeRequest request,
        CancellationToken cancellationToken)
    {
        var phone = RequirePhone(request.PhoneNumber);
        var purpose = RequirePublicPurpose(request.Purpose);
        var codeText = RequireCode(request.Code);

        var proven = await ProveAsync(phone, purpose, codeText, cancellationToken);

        var needsProfile = purpose == CustomerVerificationPurpose.Activation
            && await customers.FindByCanonicalPhoneAsync(phone, cancellationToken) is null;

        return new CustomerCodeVerifiedDto(proven.Token, proven.ExpiresAt, needsProfile);
    }

    public async Task<AuthUserDto> ActivateAsync(
        ActivateCustomerAccountRequest request,
        CancellationToken cancellationToken)
    {
        var password = ValidatePassword(request.Password);
        var verification = await SpendTokenAsync(request.VerificationToken, CustomerVerificationPurpose.Activation, cancellationToken);
        var now = clock.GetUtcNow();
        var email = NormalizeEmail(request.Email, "email");

        var customer = await customers.FindByCanonicalPhoneAsync(verification.PhoneNumber, cancellationToken);

        // Every refusal happens before anything is written, so a rejected
        // activation never leaves a half-made record behind.
        if (email is not null && await users.IsEmailTakenAsync(email, customer?.UserId, cancellationToken))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["email"] = ["This email is already used by another account. Leave it empty or use a different one."]
            });
        }

        if (customer is null)
        {
            // A first visit through the app: no front-desk record yet, so the
            // verified owner of the number becomes a new customer.
            var errors = new ValidationErrorBuilder();
            var firstName = RequireName(request.FirstName, "firstName", "First name", errors);
            var lastName = RequireName(request.LastName, "lastName", "Last name", errors);
            errors.ThrowIfInvalid();

            (customer, _) = await customers.AddOrGetByPhoneAsync(
                new Customer
                {
                    Id = Guid.NewGuid(),
                    FirstName = firstName,
                    LastName = lastName,
                    PhoneNumber = verification.PhoneNumber,
                    Email = email,
                },
                cancellationToken);
        }

        User user;

        if (customer.UserId is { } existingUserId)
        {
            user = await users.GetTrackedAsync(existingUserId, cancellationToken)
                ?? throw new InvalidOperationException($"Customer '{customer.Id}' links to a missing user.");

            if (!string.IsNullOrEmpty(user.PasswordHash) && customer.AccountStatus == CustomerAccountStatus.Active)
            {
                throw new ConflictException(
                    "This account is already active. Sign in with your mobile number, or use Forgot password to set a new password.");
            }
        }
        else
        {
            var roleId = await users.GetRoleIdAsync(RoleNames.Customer, cancellationToken)
                ?? throw new InvalidOperationException(
                    $"The '{RoleNames.Customer}' role is not present in the database. Seed the platform roles first.");

            user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = customer.FirstName,
                LastName = customer.LastName,
                IsActive = true,
            };
            user.UserRoles.Add(new UserRole { RoleId = roleId, AssignedAt = now });
            users.Add(user);
            customer.UserId = user.Id;
        }

        // The login email: one the customer just typed wins; otherwise the one
        // already on their record, when no other login uses it.
        var loginEmail = email ?? user.Email ?? customer.Email;

        if (email is null && loginEmail is not null && await users.IsEmailTakenAsync(loginEmail, user.Id, cancellationToken))
        {
            // The front desk's email for this person belongs to another login:
            // the account simply starts without one.
            loginEmail = null;
        }

        if (email is not null)
        {
            customer.Email = email;
        }

        user.Email = loginEmail;
        user.PhoneNumber = customer.PhoneNumber;
        user.PasswordHash = AuthService.HashPassword(password);
        user.SecurityStamp = VerificationSecrets.NewSecurityStamp();
        user.IsActive = true;

        customer.AccountStatus = CustomerAccountStatus.Active;
        customer.MobileVerifiedAt = now;
        verification.ConsumedAt = now;

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

    public async Task ResetPasswordAsync(ResetCustomerPasswordRequest request, CancellationToken cancellationToken)
    {
        var password = ValidatePassword(request.Password);
        var verification = await SpendTokenAsync(request.VerificationToken, CustomerVerificationPurpose.PasswordReset, cancellationToken);
        var now = clock.GetUtcNow();

        var customer = await customers.FindByCanonicalPhoneAsync(verification.PhoneNumber, cancellationToken);

        if (customer?.UserId is not { } userId
            || await users.GetTrackedAsync(userId, cancellationToken) is not { } user)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["verificationToken"] = [InvalidTokenMessage]
            });
        }

        user.PasswordHash = AuthService.HashPassword(password);

        // A new stamp signs out every session issued before the reset.
        user.SecurityStamp = VerificationSecrets.NewSecurityStamp();
        user.PhoneNumber = customer.PhoneNumber;

        // Proving the code proves the number: this is also how a customer who
        // registered by email moves to mobile sign-in.
        customer.AccountStatus = CustomerAccountStatus.Active;
        customer.MobileVerifiedAt = now;
        verification.ConsumedAt = now;

        await unitOfWork.SaveChangesAsync(cancellationToken);
    }

    public async Task<CustomerSelfDto> UpdateProfileAsync(
        Guid customerId,
        UpdateCustomerProfileRequest request,
        CancellationToken cancellationToken)
    {
        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{customerId}' was not found.");

        var errors = new ValidationErrorBuilder();
        var firstName = RequireName(request.FirstName, "firstName", "First name", errors);
        var lastName = RequireName(request.LastName, "lastName", "Last name", errors);
        errors.ThrowIfInvalid();

        var email = NormalizeEmail(request.Email, "email");

        if (customer.UserId is { } userId && await users.GetTrackedAsync(userId, cancellationToken) is { } user)
        {
            if (email is not null && await users.IsEmailTakenAsync(email, userId, cancellationToken))
            {
                throw new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["email"] = ["This email is already used by another account."]
                });
            }

            user.Email = email;
            user.FirstName = firstName;
            user.LastName = lastName;
        }

        customer.FirstName = firstName;
        customer.LastName = lastName;
        customer.Email = email;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return ToSelf(customer);
    }

    public async Task RequestMobileChangeAsync(
        Guid customerId,
        RequestMobileChangeRequest request,
        CancellationToken cancellationToken)
    {
        var phone = RequirePhone(request.PhoneNumber);

        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{customerId}' was not found.");

        if (customer.NormalizedPhoneNumber == phone)
        {
            throw PhoneError("This is already your mobile number.");
        }

        if (await customers.FindByCanonicalPhoneAsync(phone, cancellationToken) is not null)
        {
            throw PhoneError("This mobile number can't be used. If it is yours, contact the salon.");
        }

        await EnforceSendLimitsAsync(phone, CustomerVerificationPurpose.ChangeMobile, cancellationToken);

        var code = VerificationSecrets.NewCode(Policy.CodeLength);
        Store(phone, CustomerVerificationPurpose.ChangeMobile, customer.Id, code);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        await SendAsync(phone, code, CustomerVerificationPurpose.ChangeMobile, cancellationToken);
    }

    public async Task<CustomerSelfDto> ConfirmMobileChangeAsync(
        Guid customerId,
        ConfirmMobileChangeRequest request,
        CancellationToken cancellationToken)
    {
        var phone = RequirePhone(request.PhoneNumber);
        var codeText = RequireCode(request.Code);

        var pending = await codes.GetLatestPendingAsync(phone, CustomerVerificationPurpose.ChangeMobile, cancellationToken);

        // The code must have been requested by this customer for this number.
        if (pending is null || pending.CustomerId != customerId)
        {
            throw CodeError();
        }

        var proven = await ProveAsync(phone, CustomerVerificationPurpose.ChangeMobile, codeText, cancellationToken);

        var customer = await customers.GetByIdAsync(customerId, cancellationToken)
            ?? throw new NotFoundException($"Customer '{customerId}' was not found.");

        if (await customers.FindByCanonicalPhoneAsync(phone, cancellationToken) is { } holder && holder.Id != customerId)
        {
            throw PhoneError("This mobile number can't be used. If it is yours, contact the salon.");
        }

        var now = clock.GetUtcNow();

        customer.PhoneNumber = phone;
        customer.MobileVerifiedAt = now;

        if (customer.UserId is { } userId && await users.GetTrackedAsync(userId, cancellationToken) is { } user)
        {
            user.PhoneNumber = phone;
        }

        proven.Row.ConsumedAt = now;

        try
        {
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception.GetType().Name == "DbUpdateException")
        {
            // Lost a race for the number to another record: the unique index said no.
            throw PhoneError("This mobile number can't be used. If it is yours, contact the salon.");
        }

        return ToSelf(customer);
    }

    // ---- Codes --------------------------------------------------------------

    private async Task EnforceSendLimitsAsync(
        string phone,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();

        if (await codes.GetLatestCreatedAtAsync(phone, purpose, cancellationToken) is { } latest)
        {
            var wait = latest + Policy.ResendCooldown - now;

            if (wait > TimeSpan.Zero)
            {
                throw new TooManyRequestsException(
                    $"Please wait {Math.Ceiling(wait.TotalSeconds):0} seconds before requesting another code.",
                    wait);
            }
        }

        if (await codes.CountCreatedSinceAsync(phone, purpose, now.AddHours(-1), cancellationToken) >= Policy.MaxSendsPerHour)
        {
            throw new TooManyRequestsException(
                "Too many codes were requested for this number. Please try again in an hour.",
                TimeSpan.FromHours(1));
        }
    }

    private void Store(string phone, CustomerVerificationPurpose purpose, Guid? customerId, string code)
    {
        var now = clock.GetUtcNow();

        codes.Add(new CustomerVerificationCode
        {
            Id = Guid.NewGuid(),
            Purpose = purpose,
            PhoneNumber = phone,
            CustomerId = customerId,
            CodeHash = VerificationSecrets.HashCode(code),
            CreatedAt = now,
            ExpiresAt = now + Policy.CodeLifetime,
        });
    }

    private async Task SendAsync(string phone, string code, CustomerVerificationPurpose purpose, CancellationToken cancellationToken)
    {
        var minutes = (int)Math.Round(Policy.CodeLifetime.TotalMinutes);
        var action = purpose switch
        {
            CustomerVerificationPurpose.PasswordReset => "reset your password",
            CustomerVerificationPurpose.ChangeMobile => "confirm your new mobile number",
            _ => "activate your account"
        };

        try
        {
            await sender.SendAsync(
                phone,
                $"{code} is your De Bliss code to {action}. It expires in {minutes} minutes. Never share this code.",
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // The caller's answer must not change with delivery (that would leak
            // which numbers are real); the failure is for the operators.
            logger.LogError(exception, "Sending a {Purpose} code failed.", purpose);
        }
    }

    private async Task<(string Token, DateTimeOffset ExpiresAt, CustomerVerificationCode Row)> ProveAsync(
        string phone,
        CustomerVerificationPurpose purpose,
        string codeText,
        CancellationToken cancellationToken)
    {
        var now = clock.GetUtcNow();
        var pending = await codes.GetLatestPendingAsync(phone, purpose, cancellationToken);

        if (pending is null || pending.ExpiresAt <= now || pending.AttemptCount >= Policy.MaxAttempts)
        {
            throw CodeError();
        }

        if (!VerificationSecrets.CodeMatches(pending.CodeHash, codeText))
        {
            pending.AttemptCount++;
            await unitOfWork.SaveChangesAsync(cancellationToken);

            throw pending.AttemptCount >= Policy.MaxAttempts
                ? new ValidationFailedException(new Dictionary<string, string[]>
                {
                    ["code"] = ["Too many incorrect attempts. Request a new code."]
                })
                : CodeError();
        }

        var token = VerificationSecrets.NewToken();
        var expiresAt = now + Policy.VerificationTokenLifetime;

        pending.VerifiedAt = now;
        pending.TokenHash = VerificationSecrets.HashToken(token);
        pending.TokenExpiresAt = expiresAt;

        await unitOfWork.SaveChangesAsync(cancellationToken);

        return (token, expiresAt, pending);
    }

    private async Task<CustomerVerificationCode> SpendTokenAsync(
        string? token,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 200)
        {
            throw TokenError();
        }

        var row = await codes.FindByTokenHashAsync(VerificationSecrets.HashToken(token.Trim()), cancellationToken);

        if (row is null
            || row.Purpose != purpose
            || row.TokenExpiresAt is not { } expiresAt
            || expiresAt <= clock.GetUtcNow())
        {
            throw TokenError();
        }

        return row;
    }

    // ---- Validation ---------------------------------------------------------

    private static string RequirePhone(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw PhoneError("Enter your mobile number.");
        }

        if (raw.Length > CustomerLimits.PhoneMaxLength || CustomerPhone.ToCanonical(raw) is not { } phone)
        {
            throw PhoneError(CustomerPhone.InvalidMessage);
        }

        return phone;
    }

    private static CustomerVerificationPurpose RequirePublicPurpose(string? raw) =>
        raw?.Trim().ToLowerInvariant() switch
        {
            "activation" => CustomerVerificationPurpose.Activation,
            "password-reset" => CustomerVerificationPurpose.PasswordReset,
            _ => throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["purpose"] = ["Purpose must be 'activation' or 'password-reset'."]
            })
        };

    private static string RequireCode(string? raw)
    {
        var code = raw?.Trim() ?? string.Empty;

        if (code.Length is 0 or > 10 || !code.All(char.IsAsciiDigit))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["code"] = ["Enter the code from the message we sent you."]
            });
        }

        return code;
    }

    private static string ValidatePassword(string? password)
    {
        if (password is null || password.Length < PasswordMinLength)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["password"] = [$"Use at least {PasswordMinLength} characters."]
            });
        }

        if (password.Length > PasswordMaxLength)
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                ["password"] = [$"Use {PasswordMaxLength} characters or fewer."]
            });
        }

        return password;
    }

    private static string RequireName(string? raw, string field, string label, ValidationErrorBuilder errors)
    {
        var value = raw?.Trim() ?? string.Empty;

        if (value.Length == 0)
        {
            errors.Add(field, $"{label} is required.");
        }
        else if (value.Length > CustomerLimits.NameMaxLength)
        {
            errors.Add(field, $"{label} must be {CustomerLimits.NameMaxLength} characters or fewer.");
        }

        return value;
    }

    private static string? NormalizeEmail(string? raw, string field)
    {
        var email = raw?.Trim();

        if (string.IsNullOrEmpty(email))
        {
            return null;
        }

        if (email.Length > CustomerLimits.EmailMaxLength
            || !System.Net.Mail.MailAddress.TryCreate(email, out var parsed)
            || !string.Equals(parsed.Address, email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ValidationFailedException(new Dictionary<string, string[]>
            {
                [field] = ["Enter a valid email address."]
            });
        }

        return email;
    }

    private static ValidationFailedException PhoneError(string message) =>
        new(new Dictionary<string, string[]> { ["phoneNumber"] = [message] });

    private static ValidationFailedException CodeError() =>
        new(new Dictionary<string, string[]> { ["code"] = [InvalidCodeMessage] });

    private static ValidationFailedException TokenError() =>
        new(new Dictionary<string, string[]> { ["verificationToken"] = [InvalidTokenMessage] });

    internal static CustomerSelfDto ToSelf(Customer customer) =>
        new(
            customer.Id,
            customer.FirstName,
            customer.LastName,
            customer.Email,
            customer.PhoneNumber,
            customer.MobileVerifiedAt is not null);
}
