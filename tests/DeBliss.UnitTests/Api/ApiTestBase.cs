using System.Net.Http.Json;
using System.Text.Json;
using DeBliss.Api.Security;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// One API host and one throwaway database per test, so no test inherits another
/// test's rows.
///
/// Every module endpoint now sits behind authentication, so the shared <see cref="Client"/>
/// is signed in as the default owner before a test runs — existing tests exercise
/// exactly what production serves. Tests that assert the unauthenticated behaviour
/// use <see cref="AnonymousClient"/>. The sign-in is synchronous because xUnit
/// constructors cannot await; the calls are in-memory (TestServer), so there is
/// no deadlock risk.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected ApiTestBase()
    {
        Factory = new DeBlissApiFactory();
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        Factory.EnsureDefaultUserAsync().GetAwaiter().GetResult();
        AuthenticateAsync(Client, DeBlissApiFactory.DefaultUserEmail, DeBlissApiFactory.DefaultUserPassword)
            .GetAwaiter().GetResult();
    }

    private protected DeBlissApiFactory Factory { get; }

    /// <summary>The signed-in client (default owner). What a browser session sends.</summary>
    private protected HttpClient Client { get; }

    /// <summary>A second client with no credentials, for 401/403 assertions.</summary>
    private protected HttpClient AnonymousClient =>
        Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    /// <summary>Signs a client in through the real login endpoint and attaches the session cookie.</summary>
    private protected static async Task AuthenticateAsync(HttpClient client, string email, string password)
    {
        var response = await client.PostAsJsonAsync("/api/auth/login", new { email, password });

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException(
                $"Login returned {(int)response.StatusCode}: {errorBody}");
        }

        var cookie = response.Headers.GetValues("Set-Cookie")
            .Select(value => value.Split(';')[0])
            .Single(value => value.StartsWith($"{AuthToken.CookieName}=", StringComparison.Ordinal));

        client.DefaultRequestHeaders.Add("Cookie", cookie);
    }

    private protected static async Task<T> ReadAsync<T>(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();

        return JsonSerializer.Deserialize<T>(body, Json)
            ?? throw new InvalidOperationException(
                $"{(int)response.StatusCode} {response.StatusCode} returned an empty body.");
    }

    /// <summary>
    /// The path a 201 points at, so a test can read the new resource back without
    /// caring whether the framework wrote an absolute or a relative Location.
    /// </summary>
    private protected static string LocationPathOf(HttpResponseMessage response)
    {
        var location = response.Headers.Location
            ?? throw new InvalidOperationException("201 Created without a Location header.");

        return location.IsAbsoluteUri ? location.AbsolutePath : location.OriginalString;
    }

    /// <summary>
    /// Declared here rather than reusing the ASP.NET Core problem classes so the
    /// tests assert against the wire format a client actually receives.
    /// </summary>
    private protected sealed record Problem(string? Type, string? Title, int? Status, string? Detail, string? Instance);

    private protected sealed record ValidationProblem(
        string? Type,
        string? Title,
        int? Status,
        string? Detail,
        Dictionary<string, string[]> Errors);

    public void Dispose()
    {
        Client.Dispose();
        Factory.Dispose();
        GC.SuppressFinalize(this);
    }
}
