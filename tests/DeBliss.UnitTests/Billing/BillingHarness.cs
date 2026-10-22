using DeBliss.Application.Billing;
using DeBliss.Application.Settings;
using DeBliss.Application.Settings.Dtos;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Billing;

/// <summary>
/// Wires the real billing services to the real repositories over a throwaway
/// SQLite database. One context for the whole harness mirrors one web request,
/// where DI hands every repository and the unit of work the same scoped DbContext.
/// The salon starts at a zero VAT rate so the discount, bundle and payment tests
/// assert exactly what they are about; the VAT and tip tests set the rate they
/// need through <see cref="Settings"/>.
/// </summary>
internal sealed class BillingHarness : IDisposable
{
    public BillingHarness(SqliteTestDatabase? database = null)
    {
        Database = database ?? new SqliteTestDatabase();
        Context = Database.CreateContext();

        Invoices = new InvoiceService(
            new InvoiceRepository(Context),
            new CouponRepository(Context),
            new AppointmentRepository(Context),
            new CustomerRepository(Context),
            new InventoryRepository(Context),
            new SalonSettingsRepository(Context),
            Context,
            Database.Clock);

        Coupons = new CouponService(new CouponRepository(Context), Context, Database.Clock);
        Settings = new SettingsService(new SalonSettingsRepository(Context), Context);

        Settings.UpdateTaxAsync(new SaveTaxSettingsRequest(0m, false)).GetAwaiter().GetResult();
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IInvoiceService Invoices { get; }

    public ICouponService Coupons { get; }

    public ISettingsService Settings { get; }

    /// <summary>Configures the salon's VAT rate and pricing mode for the next checkout.</summary>
    public Task SetTaxAsync(decimal rate, bool pricesIncludeTax) =>
        Settings.UpdateTaxAsync(new SaveTaxSettingsRequest(rate, pricesIncludeTax));

    /// <summary>A separate context, so assertions read what was actually written.</summary>
    public DeBlissDbContext Reopen() => Database.CreateContext();

    public async Task<Guid> SeedCustomerAsync(string firstName = "Layla", string lastName = "Haddad")
    {
        await using var context = Reopen();
        var id = await CustomerSeed.CustomerAsync(context, firstName, lastName);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedServiceAsync(string name, decimal price, int durationMinutes = 60)
    {
        await using var context = Reopen();
        // Category names are unique, so each seeded service gets its own.
        var categoryId = await CatalogueSeed.CategoryAsync(context, $"{name} Category");
        var id = await CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes: durationMinutes);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedAppointmentAsync(
        Guid? customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        string? walkInCustomerName = null,
        IReadOnlyList<(Guid ServiceId, string SnapshotName, decimal UnitPrice, int SortOrder)>? lines = null,
        Guid? serviceBundleId = null,
        decimal? bundlePriceSnapshot = null,
        IReadOnlyList<Guid?>? lineStaffIds = null)
    {
        await using var context = Reopen();
        var id = await BillingSeed.AppointmentAsync(
            context, customerId, startsAt, status, walkInCustomerName, lines,
            serviceBundleId, bundlePriceSnapshot, lineStaffIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedStaffAsync(string firstName, string lastName = "Stylist")
    {
        await using var context = Reopen();
        var id = await StaffingSeed.StaffAsync(context, firstName, lastName);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedBundleAsync(string name, decimal price, IReadOnlyList<Guid> serviceIds)
    {
        await using var context = Reopen();
        // Category names are unique, so the bundle gets its own.
        var categoryId = await CatalogueSeed.CategoryAsync(context, $"{name} Category");
        var id = await CatalogueSeed.BundleAsync(context, categoryId, name, price, serviceIds);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCouponAsync(
        string code,
        DiscountType discountType = DiscountType.Fixed,
        decimal discountValue = 10m,
        bool isActive = true,
        DateOnly? validFrom = null,
        DateOnly? validUntil = null)
    {
        await using var context = Reopen();
        var id = await BillingSeed.CouponAsync(
            context, code, discountType, discountValue, isActive, validFrom, validUntil);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedInvoiceAsync(string invoiceNumber)
    {
        await using var context = Reopen();
        var id = await BillingSeed.InvoiceAsync(context, invoiceNumber);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
