using DeBliss.Application.Billing;
using DeBliss.Application.Customers;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Identity;
using DeBliss.Application.Scheduling;
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
        services.AddScoped<IServiceBundleService, ServiceBundleService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IStaffScheduleService, StaffScheduleService>();
        services.AddScoped<IStaffAvailabilityService, StaffAvailabilityService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<IReceptionistDashboardService, ReceptionistDashboardService>();
        services.AddScoped<IAuthService, AuthService>();

        return services;
    }
}
