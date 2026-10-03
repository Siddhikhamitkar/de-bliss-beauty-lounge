using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace DeBliss.Infrastructure.Persistence;

/// <summary>
/// Lets the EF Core tools build the model without booting the API. No connection is
/// opened at design time, and the fallback string contains no credentials: local
/// development uses Windows authentication.
/// </summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<DeBlissDbContext>
{
    public DeBlissDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__DeBlissDb")
            ?? "Server=(local);Database=DeBlissDb;Integrated Security=true;TrustServerCertificate=true";

        var options = new DbContextOptionsBuilder<DeBlissDbContext>()
            .UseSqlServer(connectionString)
            .Options;

        return new DeBlissDbContext(options);
    }
}
