using DeBliss.Application.Billing.Ports;
using DeBliss.Application.Common.Interfaces;
using DeBliss.Application.Customers.Ports;
using DeBliss.Application.Dashboard.Ports;
using DeBliss.Application.Identity.Ports;
using DeBliss.Application.Inventory.Ports;
using DeBliss.Application.Notifications;
using DeBliss.Application.Notifications.Ports;
using DeBliss.Application.Scheduling;
using DeBliss.Application.Scheduling.Ports;
using DeBliss.Application.ServiceCatalogue.Ports;
using DeBliss.Application.Staffing.Ports;
using DeBliss.Infrastructure.Notifications;
using DeBliss.Infrastructure.Scheduling;
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

        services.AddSingleton<ISalonTimeZone, ConfiguredSalonTimeZone>();

        services.AddScoped<IServiceCategoryRepository, ServiceCategoryRepository>();
        services.AddScoped<IServiceRepository, ServiceRepository>();
        services.AddScoped<IServiceBundleRepository, ServiceBundleRepository>();
        services.AddScoped<IStaffDirectory, StaffDirectory>();
        services.AddScoped<IStaffRepository, StaffRepository>();
        services.AddScoped<IScheduleRepository, ScheduleRepository>();
        services.AddScoped<IStaffAttendanceRepository, StaffAttendanceRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IAppointmentRepository, AppointmentRepository>();
        services.AddScoped<IAppointmentConflictPort, AppointmentRepository>();
        services.AddScoped<IStaffAppointmentPort, AppointmentRepository>();
        services.AddScoped<IBookingClaimRepository, BookingClaimRepository>();
        services.AddScoped<IInvoiceRepository, InvoiceRepository>();
        services.AddScoped<ICouponRepository, CouponRepository>();
        services.AddScoped<IInventoryRepository, InventoryRepository>();
        services.AddScoped<IDashboardReadStore, DashboardReadStore>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IAppointmentReminderRepository, AppointmentReminderRepository>();
        services.AddScoped<IReminderRuleRepository, ReminderRuleRepository>();

        // One identity per process run: the machine + PID + a run guid make
        // stale claims from an earlier process distinguishable from live ones.
        services.AddSingleton(ReminderWorkerIdentity.Create());

        services.AddScoped(provider =>
        {
            var salonName = configuration["Reminders:SalonName"];

            return new ReminderMessageComposer(
                string.IsNullOrWhiteSpace(salonName) ? "DeBliss" : salonName,
                provider.GetRequiredService<TimeProvider>());
        });

        // The WhatsApp delivery port: the only Twilio-facing registration.
        // The options read the 'Twilio' section raw so credentials arrive via
        // environment variables, and the hosted service drives the dispatch
        // loop in the background.
        services.AddSingleton(TwilioWhatsAppOptions.FromConfiguration(configuration));
        services.AddSingleton<INotificationProvider, TwilioWhatsAppProvider>();
        services.AddHostedService<ReminderDispatchWorker>();

        return services;
    }
}
