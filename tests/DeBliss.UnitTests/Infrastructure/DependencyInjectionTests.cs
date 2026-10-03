using DeBliss.Application.Common.Interfaces;
using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.UnitTests.Infrastructure;

public class DependencyInjectionTests
{
    private const string LocalConnectionString =
        "Server=(local);Database=DeBlissDb;Integrated Security=true;TrustServerCertificate=true";

    [Fact]
    public void The_unit_of_work_resolves_to_the_DbContext()
    {
        using var provider = BuildProvider(LocalConnectionString);
        using var scope = provider.CreateScope();

        Assert.Same(
            scope.ServiceProvider.GetRequiredService<DeBlissDbContext>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>());
    }

    [Fact]
    public void The_context_uses_the_Sql_Server_provider()
    {
        using var provider = BuildProvider(LocalConnectionString);
        using var scope = provider.CreateScope();
        using var context = scope.ServiceProvider.GetRequiredService<DeBlissDbContext>();

        Assert.Equal("Microsoft.EntityFrameworkCore.SqlServer", context.Database.ProviderName);
    }

    [Fact]
    public void The_audit_interceptor_is_attached_to_the_context()
    {
        using var provider = BuildProvider(LocalConnectionString);
        using var scope = provider.CreateScope();

        var interceptor = scope.ServiceProvider.GetRequiredService<AuditableEntityInterceptor>();
        var options = scope.ServiceProvider.GetRequiredService<DbContextOptions<DeBlissDbContext>>();

        Assert.Contains(interceptor, options.FindExtension<CoreOptionsExtension>()!.Interceptors!);
    }

    [Fact]
    public void A_missing_connection_string_fails_at_startup_rather_than_on_first_use()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => new ServiceCollection().AddInfrastructure(new ConfigurationBuilder().Build()));

        Assert.Contains(DependencyInjection.ConnectionStringName, exception.Message);
    }

    private static ServiceProvider BuildProvider(string? connectionString)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{DependencyInjection.ConnectionStringName}"] = connectionString,
            })
            .Build();

        return new ServiceCollection()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }
}
