using DeBliss.Application.Customers;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.Staffing;
using Microsoft.Extensions.DependencyInjection;

namespace DeBliss.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IServiceCategoryService, ServiceCategoryService>();
        services.AddScoped<IServiceCatalogueService, ServiceCatalogueService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IStaffScheduleService, StaffScheduleService>();
        services.AddScoped<IStaffAvailabilityService, StaffAvailabilityService>();
        services.AddScoped<ICustomerService, CustomerService>();

        return services;
    }
}
