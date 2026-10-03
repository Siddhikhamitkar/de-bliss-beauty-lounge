using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using DeBliss.UnitTests.TestDoubles;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Hosts the real API pipeline — routing, model binding, the exception handler —
/// over a throwaway SQLite database, so a test drives exactly what a browser would
/// without ever touching the SQL Server instance the application is configured for.
/// </summary>
internal sealed class DeBlissApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteTestDatabase _database = new();

    public SqliteTestDatabase Database => _database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps appsettings.Development.json — and therefore the real
        // SQL Server connection string — out of the host configuration.
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            $"ConnectionStrings:{DependencyInjection.ConnectionStringName}",
            _database.ConnectionString);

        builder.ConfigureTestServices(services =>
        {
            ReplaceSqlServerWithSqlite(services);
        });
    }

    private void ReplaceSqlServerWithSqlite(IServiceCollection services)
    {
        services.RemoveAll<DeBlissDbContext>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<DbContextOptions<DeBlissDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<DeBlissDbContext>>();

        services.AddDbContext<DeBlissDbContext>((provider, options) => options
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
    }

    public Task<Guid> SeedStaffAsync(string firstName, string lastName, bool isActive = true) =>
        WithNewContextAsync(context => CatalogueSeed.StaffAsync(context, firstName, lastName, isActive));

    public Task<Guid> SeedCategoryAsync(
        string name,
        bool isActive = true,
        int sortOrder = 0,
        string? description = null) =>
        WithNewContextAsync(context =>
            CatalogueSeed.CategoryAsync(context, name, isActive, sortOrder, description));

    public Task<Guid> SeedServiceAsync(
        Guid categoryId,
        string name,
        decimal price,
        int durationMinutes = 30,
        bool isActive = true,
        string? description = null,
        IReadOnlyList<Guid>? staffIds = null,
        int sortOrder = 0) =>
        WithNewContextAsync(context => CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, isActive, description, staffIds, sortOrder));

    private async Task<Guid> WithNewContextAsync(Func<DeBlissDbContext, Task<Guid>> seed)
    {
        await using var context = _database.CreateContext();
        return await seed(context);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _database.Dispose();
        }
    }
}
