using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using DeBliss.Infrastructure.Persistence.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "DeBlissDb";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<AuditableEntityInterceptor>();

        services.AddDbContext<DeBlissDbContext>((provider, options) => options
            .UseSqlServer(connectionString, sql =>
                sql.MigrationsAssembly(typeof(DeBlissDbContext).Assembly.FullName))
            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));

        services.AddScoped<IUnitOfWork>(provider => provider.GetRequiredService<DeBlissDbContext>());

        services.AddScoped<IServiceCategoryRepository, ServiceCategoryRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IStaffDirectory, StaffDirectory>();

        return services;
    }
}
