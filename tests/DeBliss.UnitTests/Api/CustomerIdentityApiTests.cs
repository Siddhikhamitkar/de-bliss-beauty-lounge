using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Customer self-service identity over the real pipeline: registration (role
/// assignment, input validation, email uniqueness, front-desk linking), customer
/// login and logout, the token-derived ownership behind /api/customer/me, and
/// the walls between the customer role and the staff, receptionist and owner
/// surfaces — with the existing owner sign-in still working alongside.
/// </summary>
public sealed class CustomerIdentityApiTests : ApiTestBase
{
    private const string Password = "Test#Customer123";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public CustomerIdentityApiTests()
    {
        Factory.SeedRoleAsync(RoleNames.Customer).GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Registration_creates_a_customer_account_with_only_the_customer_role()
    {
        using var client = AnonymousClient;

        var response = await RegisterAsync(client, "new@debliss.test", phoneNumber: "+971 50 123 4567");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);

        var user = await ReadAsync<AuthUserDto>(response);

        Assert.Equal([RoleNames.Customer], user.Roles);
        Assert.Null(user.StaffId);
        Assert.NotNull(user.CustomerId);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Users
            .Include(each => each.UserRoles)
            .ThenInclude(each => each.Role)
            .SingleAsync(each => each.Email == "new@debliss.test");

        Assert.Equal([RoleNames.Customer], stored.UserRoles.Select(each => each.Role.Name).ToList());
        Assert.False(string.IsNullOrEmpty(stored.PasswordHash));
        Assert.NotEqual(Password, stored.PasswordHash);

        var customer = await db.Customers.SingleAsync(each => each.Id == user.CustomerId);

        Assert.Equal(stored.Id, customer.UserId);
        Assert.Equal("new@debliss.test", customer.Email);
    }

    [Fact]
    public async Task Registration_never_honours_a_role_the_client_sends()
    {
        using var client = AnonymousClient;

        var response = await client.PostAsJsonAsync("/api/public/customers/register", new
        {
            firstName = "Sneaky",
            lastName = "Attempt",
            email = "sneaky@debliss.test",
            password = Password,
            roles = new[] { RoleNames.Owner },
        }, Json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var db = Factory.Database.CreateContext();
        var stored = await db.Users
            .Include(each => each.UserRoles)
            .ThenInclude(each => each.Role)
            .SingleAsync(each => each.Email == "sneaky@debliss.test");

        Assert.Equal([RoleNames.Customer], stored.UserRoles.Select(each => each.Role.Name).ToList());
    }

    [Fact]
    public async Task Registration_adopts_a_single_matching_front_desk_customer()
    {
        var seededCustomerId = await Factory.SeedCustomerAsync("Amira", "Haddad", email: "amira@debliss.test");
        using var client = AnonymousClient;

        var response = await RegisterAsync(client, "amira@debliss.test");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await ReadAsync<AuthUserDto>(response);

        Assert.Equal(seededCustomerId, user.CustomerId);

        await using var db = Factory.Database.CreateContext();
        var customer = await db.Customers.SingleAsync(each => each.Id == seededCustomerId);

        // Front-desk data survives; only the portal link is new. No duplicate row.
        Assert.Equal("Amira", customer.FirstName);
        Assert.Equal("Haddad", customer.LastName);
        Assert.NotNull(customer.UserId);
        Assert.Equal(1, await db.Customers.CountAsync(each => each.Email == "amira@debliss.test"));
    }

    [Fact]
    public async Task Registration_with_two_matching_front_desk_records_creates_a_fresh_customer()
    {
        var first = await Factory.SeedCustomerAsync("One", "Twin", email: "twin@debliss.test");
        var second = await Factory.SeedCustomerAsync("Two", "Twin", email: "twin@debliss.test");
        using var client = AnonymousClient;

        var response = await RegisterAsync(client, "twin@debliss.test");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var user = await ReadAsync<AuthUserDto>(response);

        Assert.NotNull(user.CustomerId);
        Assert.NotEqual(first, user.CustomerId);
        Assert.NotEqual(second, user.CustomerId);

        await using var db = Factory.Database.CreateContext();

        Assert.Null((await db.Customers.SingleAsync(each => each.Id == first)).UserId);
        Assert.Null((await db.Customers.SingleAsync(each => each.Id == second)).UserId);
    }

    [Fact]
    public async Task Registration_with_an_existing_account_conflicts()
    {
        using var client = AnonymousClient;
        await RegisterAsync(client, "dupe@debliss.test");

        var response = await RegisterAsync(client, "dupe@debliss.test");

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal("An account with this email already exists.", problem.Detail);
    }

    [Fact]
    public async Task Registration_rejects_invalid_input()
    {
        using var client = AnonymousClient;

        var response = await client.PostAsJsonAsync("/api/public/customers/register", new
        {
            email = "not-an-email",
            password = "short",
        }, Json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.NotEmpty(problem.Errors);
    }

    [Fact]
    public async Task Registration_rejects_an_unusable_mobile_number()
    {
        using var client = AnonymousClient;

        var response = await RegisterAsync(client, "phone@debliss.test", phoneNumber: "12");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var problem = await ReadAsync<ValidationProblem>(response);

        Assert.Contains(
            problem.Errors,
            pair => pair.Key.Equals("phoneNumber", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task A_registered_customer_can_log_in_and_receive_the_customer_link()
    {
        using var client = AnonymousClient;
        var registered = await RegisterSuccessfullyAsync(client, "login@debliss.test");

        using var loginClient = AnonymousClient;
        var login = await loginClient.PostAsJsonAsync("/api/auth/login", new
        {
            email = "login@debliss.test",
            password = Password,
        }, Json);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);

        var user = await ReadAsync<AuthUserDto>(login);

        Assert.Equal([RoleNames.Customer], user.Roles);
        Assert.Equal(registered.CustomerId, user.CustomerId);
    }

    [Fact]
    public async Task Me_returns_the_signed_in_customer_s_own_profile()
    {
        using var client = AnonymousClient;
        await RegisterAsync(client, "me@debliss.test", phoneNumber: "+971 55 987 6543");
        await AuthenticateAsync(client, "me@debliss.test", Password);

        var response = await client.GetAsync("/api/customer/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);

        var profile = await ReadAsync<CustomerSelfDto>(response);

        Assert.Equal("Test", profile.FirstName);
        Assert.Equal("Customer", profile.LastName);
        Assert.Equal("me@debliss.test", profile.Email);
        Assert.Equal("+971 55 987 6543", profile.PhoneNumber);
    }

    [Fact]
    public async Task Me_without_credentials_is_rejected()
    {
        using var client = AnonymousClient;

        var response = await client.GetAsync("/api/customer/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_sign_in_without_a_customer_link_cannot_use_the_customer_surface()
    {
        // The base client is signed in as the owner, whose token carries no
        // customer link — the policy must refuse, not serve an empty view.
        var response = await Client.GetAsync("/api/customer/me");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task A_customer_cannot_reach_owner_operational_or_staff_surfaces()
    {
        using var client = AnonymousClient;
        await RegisterAsync(client, "walled@debliss.test");
        await AuthenticateAsync(client, "walled@debliss.test", Password);

        var owner = await client.GetAsync("/api/owner/workspace");
        var operational = await client.GetAsync("/api/dashboard/receptionist");
        var frontDesk = await client.GetAsync("/api/customers");
        var staffSelf = await client.GetAsync("/api/staff/me");

        Assert.Equal(HttpStatusCode.Forbidden, owner.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, operational.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, frontDesk.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, staffSelf.StatusCode);
    }

    [Fact]
    public async Task Me_always_resolves_the_calling_customer_never_one_named_elsewhere()
    {
        using var clientA = AnonymousClient;
        var a = await RegisterSuccessfullyAsync(clientA, "a@debliss.test", firstName: "Alice", lastName: "One");
        await AuthenticateAsync(clientA, "a@debliss.test", Password);

        using var clientB = AnonymousClient;
        var b = await RegisterSuccessfullyAsync(clientB, "b@debliss.test", firstName: "Bella", lastName: "Two");
        await AuthenticateAsync(clientB, "b@debliss.test", Password);

        var mine = await clientA.GetAsync("/api/customer/me");

        Assert.Equal(HttpStatusCode.OK, mine.StatusCode);

        var profile = await ReadAsync<CustomerSelfDto>(mine);

        Assert.Equal(a.CustomerId, profile.Id);
        Assert.NotEqual(b.CustomerId, profile.Id);

        // The staff front-desk route to another customer's record stays closed.
        var frontDesk = await clientA.GetAsync($"/api/customers/{b.CustomerId}");

        Assert.Equal(HttpStatusCode.Forbidden, frontDesk.StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_a_customer_session()
    {
        using var client = AnonymousClient;
        await RegisterAsync(client, "out@debliss.test");
        await AuthenticateAsync(client, "out@debliss.test", Password);

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/customer/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);

        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // The client still replays its stored cookie header, but the token it
        // carries is revoked, so the surface must refuse.
        var after = await client.GetAsync("/api/customer/me");

        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    [Fact]
    public async Task Existing_staff_sign_in_keeps_working_alongside_customer_accounts()
    {
        using var client = AnonymousClient;
        await RegisterAsync(client, "alongside@debliss.test");

        // The owner session predates the customer account and still reaches the
        // front desk unchanged afterwards.
        var response = await Client.GetAsync("/api/customers");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static Task<HttpResponseMessage> RegisterAsync(
        HttpClient client,
        string email,
        string? phoneNumber = null,
        string firstName = "Test",
        string lastName = "Customer")
    {
        return client.PostAsJsonAsync("/api/public/customers/register", new
        {
            firstName,
            lastName,
            email,
            password = Password,
            phoneNumber,
        }, Json);
    }

    private static async Task<AuthUserDto> RegisterSuccessfullyAsync(
        HttpClient client,
        string email,
        string? phoneNumber = null,
        string firstName = "Test",
        string lastName = "Customer")
    {
        var response = await RegisterAsync(client, email, phoneNumber, firstName, lastName);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Registration returned {(int)response.StatusCode}: {errorBody}");
        }

        return await ReadAsync<AuthUserDto>(response);
    }
}
