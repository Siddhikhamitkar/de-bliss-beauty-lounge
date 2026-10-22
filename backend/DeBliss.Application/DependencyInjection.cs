using DeBliss.Application.Billing;
using DeBliss.Application.Customers;
using DeBliss.Application.Dashboard;
using DeBliss.Application.Identity;
using DeBliss.Application.Inventory;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Application.Scheduling;
using DeBliss.Application.ServiceCatalogue;
using DeBliss.Application.Settings;
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
        services.AddScoped<IStaffAttendanceService, StaffAttendanceService>();
        services.AddScoped<IStaffMobileService, StaffMobileService>();
        services.AddScoped<ICustomerService, CustomerService>();
        services.AddScoped<IAppointmentService, AppointmentService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<ICustomerBookingService, CustomerBookingService>();
        services.AddScoped<IPublicAvailabilityService, PublicAvailabilityService>();
        services.AddScoped<IInvoiceService, InvoiceService>();
        services.AddScoped<ICouponService, CouponService>();
        services.AddScoped<ISettingsService, SettingsService>();
        services.AddScoped<IInventoryService, InventoryService>();
        services.AddScoped<IReceptionistDashboardService, ReceptionistDashboardService>();
        services.AddScoped<IOwnerDashboardService, OwnerDashboardService>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<ICustomerAccessService, CustomerAccessService>();
        services.AddScoped<ReminderPlanner>();
        services.AddScoped<IReminderDispatchService, ReminderDispatchService>();
        services.AddScoped<IReminderSettingsService, ReminderSettingsService>();
        services.AddScoped<IReminderHistoryService, ReminderHistoryService>();

        return services;
    }
}
