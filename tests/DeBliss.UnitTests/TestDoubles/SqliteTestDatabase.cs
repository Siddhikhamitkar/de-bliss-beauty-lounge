using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// A throwaway database per test, disposed with the fixture.
///
/// SQLite rather than the EF InMemory provider because the real model carries
/// relational configuration — table names, column lengths, decimal precision,
/// unique indexes, cascade rules — that InMemory silently ignores. These tests are
/// about rules the database also enforces, so they need a provider that translates
/// to SQL the same way SQL Server does.
///
/// Each instance gets its own shared-cache database name and holds one connection
/// open for its lifetime; that is what keeps an in-memory SQLite database alive
/// between the short-lived connections each DbContext opens.
/// </summary>
internal sealed class SqliteTestDatabase : IDisposable
{
    private readonly SqliteConnection _keepAlive;

    public SqliteTestDatabase()
    {
        ConnectionString = $"DataSource=debliss_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        Clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 3, 9, 0, 0, TimeSpan.Zero));

        _keepAlive = new SqliteConnection(ConnectionString);
        _keepAlive.Open();

        using var context = new DeBlissDbContext(BuildOptions());
        context.Database.EnsureCreated();
    }

    public string ConnectionString { get; }

    public FakeTimeProvider Clock { get; }

    public DeBlissDbContext CreateContext() => new(BuildOptions());

    private DbContextOptions<DeBlissDbContext> BuildOptions() =>
        new DbContextOptionsBuilder<DeBlissDbContext>()
            .UseSqlite(ConnectionString)
            .AddInterceptors(new AuditableEntityInterceptor(Clock))
            .Options;

    /// <summary>
    /// Deliberately does not call <see cref="SqliteConnection.ClearAllPools"/>:
    /// that is process-wide and would pull the database out from under other
    /// test classes running in parallel.
    /// </summary>
    public void Dispose() => _keepAlive.Dispose();
}
