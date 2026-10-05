using DeBliss.Application.Common.Interfaces;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence;

/// <summary>
/// Single EF Core model for the modular monolith. Modules keep their entity
/// configurations in their own folder under <c>Persistence/Configurations</c>, and
/// all of them are applied to this one context.
/// </summary>
public class DeBlissDbContext(DbContextOptions<DeBlissDbContext> options)
    : DbContext(options), IUnitOfWork
{
    public DbSet<User> Users => Set<User>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<UserRole> UserRoles => Set<UserRole>();

    public DbSet<Customer> Customers => Set<Customer>();

    public DbSet<CustomerNote> CustomerNotes => Set<CustomerNote>();

    public DbSet<Staff> Staff => Set<Staff>();

    public DbSet<StaffService> StaffServices => Set<StaffService>();

    public DbSet<StaffWorkingHour> StaffWorkingHours => Set<StaffWorkingHour>();

    public DbSet<StaffTimeOff> StaffTimeOff => Set<StaffTimeOff>();

    public DbSet<ServiceCategory> ServiceCategories => Set<ServiceCategory>();

    public DbSet<Service> Services => Set<Service>();

    public DbSet<ServiceBundle> ServiceBundles => Set<ServiceBundle>();

    public DbSet<ServiceBundleItem> ServiceBundleItems => Set<ServiceBundleItem>();

    public DbSet<Appointment> Appointments => Set<Appointment>();

    public DbSet<AppointmentService> AppointmentServices => Set<AppointmentService>();

    public DbSet<AppointmentStatusHistory> AppointmentStatusHistory => Set<AppointmentStatusHistory>();

    public DbSet<BlockedTime> BlockedTime => Set<BlockedTime>();

    public DbSet<Invoice> Invoices => Set<Invoice>();

    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();

    public DbSet<Payment> Payments => Set<Payment>();

    public DbSet<Coupon> Coupons => Set<Coupon>();

    public DbSet<ReminderRule> ReminderRules => Set<ReminderRule>();

    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeBlissDbContext).Assembly);
    }
}
