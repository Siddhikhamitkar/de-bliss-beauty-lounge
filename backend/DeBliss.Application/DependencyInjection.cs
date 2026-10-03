using DeBliss.Application.ServiceCatalogue;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IServiceCategoryService, ServiceCategoryService>();
        services.AddScoped<IServiceCatalogueService, ServiceCatalogueService>();

        return services;
    }
}
