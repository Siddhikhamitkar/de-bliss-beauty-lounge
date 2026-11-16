using System.Net;
using System.Net.Http.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using DeBliss.Domain.Customers;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Customer identity over the real pipeline, built on proving the mobile number
/// with a one-time code: activation of front-desk records and brand-new sign-ups,
/// mobile-number sign-in, password reset (which ends old sessions), the move of
/// existing email customers to mobile sign-in, changing the number, the profile,
/// the anti-enumeration and rate rules, and the walls between the customer role
/// and the team surfaces — with team sign-in unchanged alongside.
/// </summary>
public sealed class CustomerIdentityApiTests : ApiTestBase
{
    private const string Password = "Test#Customer123";
    private const string NewPassword = "Brand#NewPass456";

    public CustomerIdentityApiTests()
    {
        Factory.SeedRoleAsync(RoleNames.Customer).GetAwaiter().GetResult();
    }

    // ---- Activation ---------------------------------------------------------

    [Fact]
    public async Task A_new_number_activates_into_a_customer_account_with_only_the_customer_role_and_no_email()
    {
        using var client = AnonymousClient;
        await RequestCodeAsync(client, "050 777 1234", "activation");

        var verified = await VerifyAsync(client, "050 777 1234", "activation");
        Assert.True(verified.NeedsProfile);

        var activate = await client.PostAsJsonAsync("/api/public/customer-auth/activate", new
        {
            verified.VerificationToken,
            password = Password,
            firstName = "Noor",
            lastName = "Saleh",
            roles = new[] { RoleNames.Owner },
        });

        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var user = await ReadAsync<AuthUserDto>(activate);

        Assert.Equal([RoleNames.Customer], user.Roles);
        Assert.Null(user.Email);
        Assert.NotNull(user.CustomerId);

        await using var db = Factory.Database.CreateContext();
        var customer = await db.Customers.SingleAsync(row => row.Id == user.CustomerId);
        Assert.Equal("+971507771234", customer.NormalizedPhoneNumber);
        Assert.Equal(CustomerAccountStatus.Active, customer.AccountStatus);
        Assert.NotNull(customer.MobileVerifiedAt);
        Assert.Null(customer.Email);

        var roles = await db.UserRoles.Where(row => row.UserId == user.Id).Select(row => row.Role.Name).ToListAsync();
        Assert.Equal([RoleNames.Customer], roles);

        using var phoneLogin = AnonymousClient;
        await AuthenticateByPhoneAsync(phoneLogin, "+971 50 777 1234", Password);
        Assert.Equal(HttpStatusCode.OK, (await phoneLogin.GetAsync("/api/customer/me")).StatusCode);
    }

    [Fact]
    public async Task A_new_number_needs_a_name_to_activate()
    {
        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 777 2222", "activation");
        var verified = await VerifyAsync(client, "+971 50 777 2222", "activation");

        var activate = await client.PostAsJsonAsync("/api/public/customer-auth/activate", new
        {
            verified.VerificationToken,
            password = Password,
        });

        Assert.Equal(HttpStatusCode.BadRequest, activate.StatusCode);
        var problem = await ReadAsync<ValidationProblem>(activate);
        Assert.Contains("firstName", problem.Errors.Keys);
        Assert.Contains("lastName", problem.Errors.Keys);
    }

    [Fact]
    public async Task A_front_desk_customer_activates_their_existing_record_and_sees_their_history()
    {
        // Created at the desk with a local-format number; history already exists.
        var customerId = await Factory.SeedCustomerAsync("Huda", "Falasi", phoneNumber: "0501234561");
        var categoryId = await Factory.SeedCategoryAsync("Hair");
        var serviceId = await Factory.SeedServiceAsync(categoryId, "Signature Cut", 150m);
        await Factory.SeedCustomerAppointmentAsync(
            customerId, DateTimeOffset.UtcNow.AddDays(-7), lines: [(serviceId, null, "Signature Cut")]);

        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 123 4561", "activation");
        var verified = await VerifyAsync(client, "+971 50 123 4561", "activation");
        Assert.False(verified.NeedsProfile);

        var activate = await client.PostAsJsonAsync("/api/public/customer-auth/activate", new
        {
            verified.VerificationToken,
            password = Password,
        });

        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var user = await ReadAsync<AuthUserDto>(activate);
        Assert.Equal(customerId, user.CustomerId);

        await using (var db = Factory.Database.CreateContext())
        {
            Assert.Equal(1, await db.Customers.CountAsync());
        }

        using var signedIn = AnonymousClient;
        await AuthenticateByPhoneAsync(signedIn, "050 123 4561", Password);

        var history = await signedIn.GetAsync("/api/customer/appointments");
        Assert.Equal(HttpStatusCode.OK, history.StatusCode);
        Assert.Contains("Signature Cut", await history.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task A_pending_front_desk_customer_cannot_sign_in_by_phone_before_activating()
    {
        await Factory.SeedCustomerAsync("Pending", "Guest", phoneNumber: "+971 50 444 5555");

        using var client = AnonymousClient;
        var login = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "+971 50 444 5555", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    [Fact]
    public async Task Activating_an_already_active_account_conflicts_once_the_number_is_proven()
    {
        // An account that is already active (here: an existing email customer).
        await SeedLegacyEmailCustomerAsync("active@debliss.test", "+971 50 888 1111");

        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 888 1111", "activation");
        var verified = await VerifyAsync(client, "+971 50 888 1111", "activation");

        var again = await client.PostAsJsonAsync("/api/public/customer-auth/activate", new
        {
            verified.VerificationToken,
            password = NewPassword,
        });

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
    }

    [Fact]
    public async Task A_verification_token_can_be_spent_only_once()
    {
        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 999 0000", "activation");
        var verified = await VerifyAsync(client, "+971 50 999 0000", "activation");

        var body = new { verified.VerificationToken, password = Password, firstName = "One", lastName = "Time" };
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/api/public/customer-auth/activate", body)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/public/customer-auth/activate", body)).StatusCode);
    }

    // ---- Codes: enumeration, attempts, cooldown ------------------------------

    [Fact]
    public async Task A_code_request_answers_the_same_whether_or_not_the_number_has_an_account()
    {
        await ActivateCustomerAsync("+971 50 100 2000", Password);

        using var client = AnonymousClient;
        var known = await client.PostAsJsonAsync("/api/public/customer-auth/otp",
            new { phoneNumber = "+971 50 100 2000", purpose = "password-reset" });
        var unknown = await client.PostAsJsonAsync("/api/public/customer-auth/otp",
            new { phoneNumber = "+971 50 100 2999", purpose = "password-reset" });

        Assert.Equal(HttpStatusCode.Accepted, known.StatusCode);
        Assert.Equal(known.StatusCode, unknown.StatusCode);
        Assert.Equal(await known.Content.ReadAsStringAsync(), await unknown.Content.ReadAsStringAsync());

        // Only the real account was actually messaged.
        Assert.Equal(0, Factory.Otp.CountFor("+971501002999"));

        // ...and the cooldown applies identically to both.
        var knownAgain = await client.PostAsJsonAsync("/api/public/customer-auth/otp",
            new { phoneNumber = "+971 50 100 2000", purpose = "password-reset" });
        var unknownAgain = await client.PostAsJsonAsync("/api/public/customer-auth/otp",
            new { phoneNumber = "+971 50 100 2999", purpose = "password-reset" });

        Assert.Equal(HttpStatusCode.TooManyRequests, knownAgain.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, unknownAgain.StatusCode);
        Assert.NotNull(unknownAgain.Headers.RetryAfter);
    }

    [Fact]
    public async Task A_wrong_code_is_refused_and_too_many_wrong_codes_kill_it()
    {
        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 321 0000", "activation");
        var realCode = Factory.Otp.LastCodeFor("+971503210000")!;
        var wrong = realCode == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var response = await client.PostAsJsonAsync("/api/public/customer-auth/otp/verify",
                new { phoneNumber = "+971 50 321 0000", purpose = "activation", code = wrong });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        // The right code no longer works: the attempts are used up.
        var late = await client.PostAsJsonAsync("/api/public/customer-auth/otp/verify",
            new { phoneNumber = "+971 50 321 0000", purpose = "activation", code = realCode });

        Assert.Equal(HttpStatusCode.BadRequest, late.StatusCode);
    }

    [Fact]
    public async Task A_code_for_one_purpose_cannot_be_used_for_another()
    {
        await ActivateCustomerAsync("+971 50 654 0000", Password);

        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 654 0000", "password-reset");

        var response = await client.PostAsJsonAsync("/api/public/customer-auth/otp/verify", new
        {
            phoneNumber = "+971 50 654 0000",
            purpose = "activation",
            code = Factory.Otp.LastCodeFor("+971506540000"),
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task An_unreadable_number_is_a_validation_error()
    {
        using var client = AnonymousClient;
        var response = await client.PostAsJsonAsync("/api/public/customer-auth/otp",
            new { phoneNumber = "12", purpose = "activation" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("phoneNumber", (await ReadAsync<ValidationProblem>(response)).Errors.Keys);
    }

    // ---- Sign-in ------------------------------------------------------------

    [Fact]
    public async Task Sign_in_takes_exactly_one_identifier()
    {
        using var client = AnonymousClient;

        var neither = await client.PostAsJsonAsync("/api/auth/login", new { password = Password });
        var both = await client.PostAsJsonAsync("/api/auth/login",
            new { email = "a@debliss.test", phoneNumber = "+971 50 000 0000", password = Password });

        Assert.Equal(HttpStatusCode.BadRequest, neither.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, both.StatusCode);
    }

    [Fact]
    public async Task A_wrong_password_and_an_unknown_number_fail_identically()
    {
        await ActivateCustomerAsync("+971 50 200 3000", Password);

        using var client = AnonymousClient;
        var wrongPassword = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "+971 50 200 3000", password = "nope-nope" });
        var unknown = await client.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "+971 50 200 3999", password = Password });

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassword.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknown.StatusCode);
        Assert.Equal(
            (await ReadAsync<Problem>(wrongPassword)).Detail,
            (await ReadAsync<Problem>(unknown)).Detail);
    }

    [Fact]
    public async Task A_team_account_cannot_sign_in_by_mobile_number()
    {
        // The owner has no customer record, so a number cannot reach it.
        using var client = AnonymousClient;
        var login = await client.PostAsJsonAsync("/api/auth/login",
            new { phoneNumber = "+971 50 000 0000", password = DeBlissApiFactory.DefaultUserPassword });

        Assert.Equal(HttpStatusCode.Unauthorized, login.StatusCode);
    }

    // ---- Password reset -----------------------------------------------------

    [Fact]
    public async Task A_password_reset_sets_the_new_password_and_ends_existing_sessions()
    {
        var (session, _) = await ActivateCustomerAsync("+971 50 300 4000", Password);
        Assert.Equal(HttpStatusCode.OK, (await session.GetAsync("/api/customer/me")).StatusCode);

        using var client = AnonymousClient;
        await RequestCodeAsync(client, "+971 50 300 4000", "password-reset");
        var verified = await VerifyAsync(client, "+971 50 300 4000", "password-reset");

        var reset = await client.PostAsJsonAsync("/api/public/customer-auth/password/reset",
            new { verified.VerificationToken, password = NewPassword });

        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // The session from before the reset is over.
        Assert.Equal(HttpStatusCode.Unauthorized, (await session.GetAsync("/api/customer/me")).StatusCode);

        using var oldPassword = AnonymousClient;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await oldPassword.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "+971 50 300 4000", password = Password })).StatusCode);

        using var newPassword = AnonymousClient;
        await AuthenticateByPhoneAsync(newPassword, "+971 50 300 4000", NewPassword);
        Assert.Equal(HttpStatusCode.OK, (await newPassword.GetAsync("/api/customer/me")).StatusCode);
        session.Dispose();
    }

    [Fact]
    public async Task An_existing_email_customer_keeps_email_sign_in_and_moves_to_mobile_sign_in_by_proving_the_number()
    {
        // An account from before mobile sign-in: email login, number on file but never verified.
        var userId = await SeedLegacyEmailCustomerAsync("legacy@debliss.test", "+971 50 123 4567");

        using var email = AnonymousClient;
        await AuthenticateAsync(email, "legacy@debliss.test", Password);
        Assert.Equal(HttpStatusCode.OK, (await email.GetAsync("/api/customer/me")).StatusCode);

        using var phoneBefore = AnonymousClient;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await phoneBefore.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "0501234567", password = Password })).StatusCode);

        // Forgot password by mobile proves the number.
        using var client = AnonymousClient;
        await RequestCodeAsync(client, "0501234567", "password-reset");
        var verified = await VerifyAsync(client, "0501234567", "password-reset");
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync(
            "/api/public/customer-auth/password/reset", new { verified.VerificationToken, password = NewPassword })).StatusCode);

        using var phoneAfter = AnonymousClient;
        await AuthenticateByPhoneAsync(phoneAfter, "+971 50 123 4567", NewPassword);

        using var emailAfter = AnonymousClient;
        await AuthenticateAsync(emailAfter, "legacy@debliss.test", NewPassword);

        var me = await ReadAsync<AuthUserDto>(await emailAfter.GetAsync("/api/auth/me"));
        Assert.Equal(userId, me.Id);
    }

    // ---- Profile ------------------------------------------------------------

    [Fact]
    public async Task A_customer_can_add_and_change_an_optional_email()
    {
        var (client, user) = await ActivateCustomerAsync("+971 50 400 5000", Password);

        var update = await client.PutAsJsonAsync("/api/customer/me",
            new { firstName = "Lina", lastName = "Haddad", email = "lina@debliss.test" });

        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        var profile = await ReadAsync<CustomerSelfDto>(update);
        Assert.Equal("lina@debliss.test", profile.Email);
        Assert.Equal("Lina", profile.FirstName);
        Assert.True(profile.MobileVerified);

        // ...and can sign in with it.
        using var byEmail = AnonymousClient;
        await AuthenticateAsync(byEmail, "lina@debliss.test", Password);

        var cleared = await client.PutAsJsonAsync("/api/customer/me", new { firstName = "Lina", lastName = "Haddad", email = (string?)null });
        Assert.Null((await ReadAsync<CustomerSelfDto>(cleared)).Email);
        Assert.Equal(user.CustomerId, (await ReadAsync<CustomerSelfDto>(await client.GetAsync("/api/customer/me"))).Id);
        client.Dispose();
    }

    [Fact]
    public async Task Changing_the_mobile_number_needs_the_code_sent_to_the_new_number()
    {
        var (client, _) = await ActivateCustomerAsync("+971 50 500 6000", Password);

        Assert.Equal(HttpStatusCode.Accepted,
            (await client.PostAsJsonAsync("/api/customer/me/phone/otp", new { phoneNumber = "+971 55 500 6000" })).StatusCode);

        // Nothing changes on a wrong code.
        var wrong = await client.PostAsJsonAsync("/api/customer/me/phone/verify",
            new { phoneNumber = "+971 55 500 6000", code = "000000" == Factory.Otp.LastCodeFor("+971555006000") ? "111111" : "000000" });
        Assert.Equal(HttpStatusCode.BadRequest, wrong.StatusCode);

        var confirm = await client.PostAsJsonAsync("/api/customer/me/phone/verify",
            new { phoneNumber = "+971 55 500 6000", code = Factory.Otp.LastCodeFor("+971555006000") });

        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode);
        Assert.Equal("+971555006000", (await ReadAsync<CustomerSelfDto>(confirm)).PhoneNumber);

        using var newNumber = AnonymousClient;
        await AuthenticateByPhoneAsync(newNumber, "055 500 6000", Password);

        using var oldNumber = AnonymousClient;
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await oldNumber.PostAsJsonAsync("/api/auth/login", new { phoneNumber = "+971 50 500 6000", password = Password })).StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task A_number_that_belongs_to_another_customer_cannot_be_taken()
    {
        await Factory.SeedCustomerAsync("Someone", "Else", phoneNumber: "+971 56 111 2222");
        var (client, _) = await ActivateCustomerAsync("+971 50 600 7000", Password);

        var response = await client.PostAsJsonAsync("/api/customer/me/phone/otp", new { phoneNumber = "056 111 2222" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, Factory.Otp.CountFor("+971561112222"));
        client.Dispose();
    }

    // ---- Retired and unchanged surfaces --------------------------------------

    [Fact]
    public async Task The_unverified_email_registration_is_retired()
    {
        using var client = AnonymousClient;
        var response = await client.PostAsJsonAsync("/api/public/customers/register", new
        {
            firstName = "Old",
            lastName = "Way",
            email = "old@debliss.test",
            password = Password,
            phoneNumber = "+971 50 700 8000",
        });

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);

        await using var db = Factory.Database.CreateContext();
        Assert.False(await db.Customers.AnyAsync());
    }

    [Fact]
    public async Task Me_without_credentials_is_rejected()
    {
        using var client = AnonymousClient;

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/me")).StatusCode);
    }

    [Fact]
    public async Task A_sign_in_without_a_customer_link_cannot_use_the_customer_surface()
    {
        // The base client is signed in as the owner, whose token carries no
        // customer link — the policy must refuse, not serve an empty view.
        Assert.Equal(HttpStatusCode.Forbidden, (await Client.GetAsync("/api/customer/me")).StatusCode);
    }

    [Fact]
    public async Task A_customer_cannot_reach_owner_operational_or_staff_surfaces()
    {
        var (client, _) = await ActivateCustomerAsync("+971 50 800 9000", Password);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/owner/workspace")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/dashboard/receptionist")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/customers")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/staff/me")).StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Me_always_resolves_the_calling_customer_never_one_named_elsewhere()
    {
        var (clientA, a) = await ActivateCustomerAsync("+971 50 910 0001", Password, firstName: "Alice", lastName: "One");
        var (clientB, b) = await ActivateCustomerAsync("+971 50 910 0002", Password, firstName: "Bella", lastName: "Two");

        var profile = await ReadAsync<CustomerSelfDto>(await clientA.GetAsync("/api/customer/me"));

        Assert.Equal(a.CustomerId, profile.Id);
        Assert.NotEqual(b.CustomerId, profile.Id);
        Assert.Equal(HttpStatusCode.Forbidden, (await clientA.GetAsync($"/api/customers/{b.CustomerId}")).StatusCode);
        clientA.Dispose();
        clientB.Dispose();
    }

    [Fact]
    public async Task Logout_invalidates_a_customer_session()
    {
        var (client, _) = await ActivateCustomerAsync("+971 50 920 0000", Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customer/me")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/logout", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/customer/me")).StatusCode);
        client.Dispose();
    }

    [Fact]
    public async Task Team_sign_in_keeps_working_alongside_customer_accounts()
    {
        await ActivateCustomerAsync("+971 50 930 0000", Password);
        await Factory.SeedUserAsync("front@debliss.test", "Front#Desk123", RoleNames.Receptionist);

        // The owner session is untouched by customer activity...
        Assert.Equal(HttpStatusCode.OK, (await Client.GetAsync("/api/customers")).StatusCode);

        // ...and a fresh team sign-in by email still works.
        using var receptionist = AnonymousClient;
        await AuthenticateAsync(receptionist, "front@debliss.test", "Front#Desk123");
        Assert.Equal(HttpStatusCode.OK, (await receptionist.GetAsync("/api/dashboard/receptionist")).StatusCode);
    }

    // ---- Helpers ------------------------------------------------------------

    private static async Task RequestCodeAsync(HttpClient client, string phoneNumber, string purpose)
    {
        var response = await client.PostAsJsonAsync("/api/public/customer-auth/otp", new { phoneNumber, purpose });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task<CustomerCodeVerifiedDto> VerifyAsync(HttpClient client, string phoneNumber, string purpose)
    {
        var code = Factory.Otp.LastCodeFor(MobileNumber.Normalize(phoneNumber)!)
            ?? throw new InvalidOperationException($"No code was sent to {phoneNumber}.");

        var response = await client.PostAsJsonAsync("/api/public/customer-auth/otp/verify", new { phoneNumber, purpose, code });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        return await ReadAsync<CustomerCodeVerifiedDto>(response);
    }

    /// <summary>A customer account as it existed before mobile sign-in: email login, number unverified.</summary>
    private async Task<Guid> SeedLegacyEmailCustomerAsync(string email, string phoneNumber)
    {
        await using var db = Factory.Database.CreateContext();
        var userId = await IdentitySeed.UserAsync(db, email, Password, RoleNames.Customer, "Legacy", "Customer");
        var customerId = await CustomerSeed.CustomerAsync(db, "Legacy", "Customer", phoneNumber, email);

        var customer = await db.Customers.SingleAsync(row => row.Id == customerId);
        customer.UserId = userId;
        customer.AccountStatus = CustomerAccountStatus.Active;
        await db.SaveChangesAsync();

        return userId;
    }
}
