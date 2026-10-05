using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Application.Identity;
using DeBliss.Application.Identity.Dtos;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// The authentication and role boundary, exercised over the real pipeline: login
/// success/failure shape, the safe user projection, the owner-only policy, and
/// logout invalidation.
/// </summary>
public sealed class AuthApiTests : ApiTestBase
{
    private const string ReceptionistEmail = "receptionist@debliss.test";
    private const string ReceptionistPassword = "Test#Front123";
    private const string StaffEmail = "staff@debliss.test";
    private const string StaffPassword = "Test#Team123";
    private const string WrongPassword = "not-the-password";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public AuthApiTests()
    {
        Factory.SeedUserAsync(
            ReceptionistEmail, ReceptionistPassword, RoleNames.Receptionist, "Rita", "Desk").GetAwaiter().GetResult();
        Factory.SeedUserAsync(
            StaffEmail, StaffPassword, RoleNames.Staff, "Aisha", "Stylist").GetAwaiter().GetResult();
    }

    [Fact]
    public async Task Login_with_valid_credentials_succeeds_and_sets_the_session_cookie()
    {
        using var client = AnonymousClient;
        var response = await LoginAsync(client, ReceptionistEmail, ReceptionistPassword);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(
            response.Headers.GetValues("Set-Cookie"),
            value => value.StartsWith("debliss_auth=", StringComparison.Ordinal) &&
                     value.Contains("httponly", StringComparison.OrdinalIgnoreCase) &&
                     value.Contains("samesite=strict", StringComparison.OrdinalIgnoreCase));

        var user = await ReadAsync<AuthUserDto>(response);

        Assert.Equal("Rita", user.FirstName);
        Assert.Equal("Desk", user.LastName);
        Assert.Equal(ReceptionistEmail, user.Email);
        Assert.Equal([RoleNames.Receptionist], user.Roles);
    }

    [Fact]
    public async Task Login_with_the_wrong_password_fails_with_the_generic_message()
    {
        using var client = AnonymousClient;
        var response = await LoginAsync(client, ReceptionistEmail, WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal("Email or password is incorrect.", problem.Detail);
    }

    [Fact]
    public async Task Login_with_an_unknown_email_fails_with_the_same_generic_message()
    {
        using var client = AnonymousClient;
        var response = await LoginAsync(client, "nobody@debliss.test", WrongPassword);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        var problem = await ReadAsync<Problem>(response);

        Assert.Equal("Email or password is incorrect.", problem.Detail);
    }

    [Fact]
    public async Task The_password_hash_is_never_returned_by_login_or_me()
    {
        using var client = AnonymousClient;
        var login = await LoginAsync(client, ReceptionistEmail, ReceptionistPassword);

        await AssertBodyHasNoPasswordMaterial(login);
        await AssertBodyHasNoPasswordMaterial(await client.GetAsync("/api/auth/me"));
    }

    [Fact]
    public async Task Me_returns_the_signed_in_user()
    {
        using var client = AnonymousClient;
        await AuthenticateAsync(client, StaffEmail, StaffPassword);

        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var user = await ReadAsync<AuthUserDto>(response);

        Assert.Equal(StaffEmail, user.Email);
        Assert.Equal([RoleNames.Staff], user.Roles);
    }

    [Fact]
    public async Task Me_without_credentials_is_rejected()
    {
        using var client = AnonymousClient;
        var response = await client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task A_receptionist_can_use_operational_endpoints()
    {
        using var client = AnonymousClient;
        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);

        var response = await client.GetAsync("/api/service-categories");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_receptionist_cannot_reach_the_owner_workspace()
    {
        using var client = AnonymousClient;
        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);

        var response = await client.GetAsync("/api/owner/workspace");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task An_owner_can_reach_the_owner_workspace()
    {
        var response = await Client.GetAsync("/api/owner/workspace");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_staff_member_cannot_reach_the_owner_workspace_or_operational_endpoints()
    {
        using var client = AnonymousClient;
        await AuthenticateAsync(client, StaffEmail, StaffPassword);

        var ownerResponse = await client.GetAsync("/api/owner/workspace");
        var operationalResponse = await client.GetAsync("/api/dashboard/receptionist");

        Assert.Equal(HttpStatusCode.Forbidden, ownerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, operationalResponse.StatusCode);
    }

    [Fact]
    public async Task Logout_invalidates_the_session_cookie()
    {
        using var client = AnonymousClient;
        await AuthenticateAsync(client, ReceptionistEmail, ReceptionistPassword);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/auth/me")).StatusCode);

        var logout = await client.PostAsync("/api/auth/logout", null);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        // The client still replays its stored cookie header, but the response told
        // the browser to delete it; without a valid credential /me must reject.
        var meAfter = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.Unauthorized, meAfter.StatusCode);
    }

    [Fact]
    public async Task A_bearer_token_in_the_authorization_header_is_accepted()
    {
        using var client = AnonymousClient;
        var login = await LoginAsync(client, ReceptionistEmail, ReceptionistPassword);
        var cookie = login.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0])
            .Single(value => value.StartsWith("debliss_auth=", StringComparison.Ordinal));
        var token = cookie["debliss_auth=".Length..];

        using var bearerClient = AnonymousClient;
        bearerClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        var response = await bearerClient.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static async Task<HttpResponseMessage> LoginAsync(HttpClient client, string email, string password) =>
        await client.PostAsJsonAsync("/api/auth/login", new { email, password }, Json);

    private static async Task AssertBodyHasNoPasswordMaterial(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        Assert.DoesNotContain("passwordHash", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("password", body, StringComparison.OrdinalIgnoreCase);
    }
}
