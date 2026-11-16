using DeBliss.Application.Common.Interfaces;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Inventory;
using DeBliss.Domain.Notifications;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Services;
using DeBliss.Domain.Settings;
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

    public DbSet<StaffAttendance> StaffAttendances => Set<StaffAttendance>();

    public DbSet<SalarySlip> SalarySlips => Set<SalarySlip>();

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

    public DbSet<InventoryProduct> InventoryProducts => Set<InventoryProduct>();

    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    public DbSet<ReminderRule> ReminderRules => Set<ReminderRule>();

    public DbSet<AppointmentReminder> AppointmentReminders => Set<AppointmentReminder>();

    public DbSet<BookingClaim> BookingClaims => Set<BookingClaim>();

    public DbSet<SalonSettings> SalonSettings => Set<SalonSettings>();

    public DbSet<DeBliss.Domain.Loyalty.LoyaltySettings> LoyaltySettings => Set<DeBliss.Domain.Loyalty.LoyaltySettings>();

    public DbSet<DeBliss.Domain.Loyalty.CustomerLoyaltyTransaction> CustomerLoyaltyTransactions => Set<DeBliss.Domain.Loyalty.CustomerLoyaltyTransaction>();

    public DbSet<CustomerVerificationCode> CustomerVerificationCodes => Set<CustomerVerificationCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(DeBlissDbContext).Assembly);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        DeriveCustomerColumns();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        DeriveCustomerColumns();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Keeps <see cref="Customer.NormalizedPhoneNumber"/> in step with the number
    /// as entered, whichever module wrote it (front desk, booking, import,
    /// registration), so the unique index always guards the real identity key.
    /// A number that changes without proof of ownership also loses its
    /// verified mark, so it cannot be used for mobile sign-in.
    /// </summary>
    private void DeriveCustomerColumns()
    {
        foreach (var entry in ChangeTracker.Entries<Customer>())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
            {
                continue;
            }

            var normalized = MobileNumber.Normalize(entry.Entity.PhoneNumber);

            if (entry.State == EntityState.Modified
                && entry.Property(customer => customer.NormalizedPhoneNumber).OriginalValue != normalized
                && !entry.Property(customer => customer.MobileVerifiedAt).IsModified)
            {
                entry.Entity.MobileVerifiedAt = null;
            }

            entry.Entity.NormalizedPhoneNumber = normalized;
        }
    }

    public async Task<IUnitOfWorkTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        // Serializable so a booking's re-check inside the transaction cannot
        // miss a concurrent one (SQL Server takes range locks; SQLite's
        // translation is BEGIN IMMEDIATE, which serializes writers).
        var transaction = await Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        return new EntityFrameworkUnitOfWorkTransaction(transaction);
    }
}
