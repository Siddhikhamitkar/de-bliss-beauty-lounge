using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// One API host and one throwaway database per test, so no test inherits another
/// test's rows.
///
/// The class is public because xUnit insists on public test classes, but every
/// member is <c>private protected</c>: the factory and its SQLite test double stay
/// internal to this assembly.
/// </summary>
public abstract class ApiTestBase : IDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    protected ApiTestBase()
    {
        Factory = new DeBlissApiFactory();
        Client = Factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
    }

    private protected DeBlissApiFactory Factory { get; }

    private protected HttpClient Client { get; }

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
