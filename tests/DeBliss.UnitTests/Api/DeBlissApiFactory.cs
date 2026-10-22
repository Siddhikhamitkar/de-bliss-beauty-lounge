using DeBliss.Api.Security;
using DeBliss.Application.Identity;
using DeBliss.Infrastructure;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Interceptors;
using DeBliss.UnitTests.TestDoubles;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using DeBliss.Domain.Settings;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace DeBliss.UnitTests.Api;

/// <summary>
/// Hosts the real API pipeline — routing, model binding, the exception handler —
/// over a throwaway SQLite database, so a test drives exactly what a browser would
/// without ever touching the SQL Server instance the application is configured for.
/// </summary>
internal sealed class DeBlissApiFactory : WebApplicationFactory<Program>
{
    /// <summary>Test-only JWT key: random per checkout, never a real secret.</summary>
    public const string TestSigningKey =
        "debliss-testing-only-signing-key-5e8b3f12-9c6a-4d2e-b7f0-31a94c8d6e52";

    private readonly SqliteTestDatabase _database = new();

    public SqliteTestDatabase Database => _database;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // "Testing" keeps appsettings.Development.json — and therefore the real
        // SQL Server connection string — out of the host configuration.
        builder.UseEnvironment("Testing");
        builder.UseSetting(
            $"ConnectionStrings:{DependencyInjection.ConnectionStringName}",
            _database.ConnectionString);
        // ...and for the same reason the development JWT key is absent, so the
        // tests supply their own throwaway one.
        builder.UseSetting(AuthToken.SigningKeyConfigPath, TestSigningKey);

        builder.ConfigureTestServices(services =>
        {
            ReplaceSqlServerWithSqlite(services);
        });
    }

    private void ReplaceSqlServerWithSqlite(IServiceCollection services)
    {
        services.RemoveAll<DeBlissDbContext>();
        services.RemoveAll<DbContextOptions>();
        services.RemoveAll<DbContextOptions<DeBlissDbContext>>();
        services.RemoveAll<IDbContextOptionsConfiguration<DeBlissDbContext>>();

        services.AddDbContext<DeBlissDbContext>((provider, options) => options
            .UseSqlite(_database.ConnectionString)
            .AddInterceptors(provider.GetRequiredService<AuditableEntityInterceptor>()));
    }

    public Task<Guid> SeedStaffAsync(
        string firstName,
        string lastName,
        bool isActive = true,
        string? jobTitle = null,
        string? email = null,
        string? calendarColor = null) =>
        WithNewContextAsync(context =>
            CatalogueSeed.StaffAsync(context, firstName, lastName, isActive, jobTitle, email, calendarColor: calendarColor));

    public Task<Guid> SeedCategoryAsync(
        string name,
        bool isActive = true,
        int sortOrder = 0,
        string? description = null) =>
        WithNewContextAsync(context =>
            CatalogueSeed.CategoryAsync(context, name, isActive, sortOrder, description));

    public Task<Guid> SeedServiceAsync(
        Guid categoryId,
        string name,
        decimal price,
        int durationMinutes = 30,
        bool isActive = true,
        string? description = null,
        IReadOnlyList<Guid>? staffIds = null,
        int sortOrder = 0,
        bool isOnlineBookable = false) =>
        WithNewContextAsync(context => CatalogueSeed.ServiceAsync(
            context, categoryId, name, price, durationMinutes, isActive, description, staffIds, sortOrder, isOnlineBookable));

    public Task<Guid> SeedBundleAsync(
        Guid categoryId,
        string name,
        decimal price,
        IReadOnlyList<Guid> serviceIds,
        bool isActive = true,
        string? description = null,
        int sortOrder = 0) =>
        WithNewContextAsync(context => CatalogueSeed.BundleAsync(
            context, categoryId, name, price, serviceIds, isActive, description, sortOrder));

    public Task<Guid> SeedCustomerAsync(
        string firstName,
        string lastName,
        string? phoneNumber = null,
        string? email = null,
        bool isActive = true) =>
        WithNewContextAsync(context =>
            CustomerSeed.CustomerAsync(context, firstName, lastName, phoneNumber, email, isActive));

    public async Task SeedWorkingHoursAsync(Guid staffId, DayOfWeek day, TimeSpan start, TimeSpan end)
    {
        await using var context = _database.CreateContext();
        await StaffingSeed.WorkingHoursAsync(context, staffId, day, start, end);
    }

    public Task<Guid> SeedCustomerNoteAsync(Guid customerId, string note) =>
        WithNewContextAsync(context => CustomerSeed.NoteAsync(context, customerId, note));

    public Task<Guid> SeedCustomerAppointmentAsync(
        Guid customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        IReadOnlyList<(Guid ServiceId, Guid? StaffId, string SnapshotName)>? lines = null) =>
        WithNewContextAsync(context =>
            CustomerSeed.AppointmentAsync(context, customerId, startsAt, status, lines));

    public Task<Guid> SeedCouponAsync(
        string code,
        DiscountType discountType = DiscountType.Fixed,
        decimal discountValue = 10m,
        bool isActive = true,
        DateOnly? validFrom = null,
        DateOnly? validUntil = null) =>
        WithNewContextAsync(context =>
            BillingSeed.CouponAsync(context, code, discountType, discountValue, isActive, validFrom, validUntil));

    public Task<Guid> SeedBillingAppointmentAsync(
        Guid? customerId,
        DateTimeOffset startsAt,
        AppointmentStatus status = AppointmentStatus.Completed,
        string? walkInCustomerName = null,
        IReadOnlyList<(Guid ServiceId, string SnapshotName, decimal UnitPrice, int SortOrder)>? lines = null) =>
        WithNewContextAsync(context =>
            BillingSeed.AppointmentAsync(context, customerId, startsAt, status, walkInCustomerName, lines));

    /// <summary>Standard owner sign-in most tests use; created once per database.</summary>
    public const string DefaultUserEmail = "owner@debliss.test";
    public const string DefaultUserPassword = "Test#Owner123";

    public async Task SeedUserAsync(
        string email,
        string password,
        string roleName,
        string firstName = "Test",
        string lastName = "User",
        bool isActive = true,
        Guid? linkedStaffId = null)
    {
        await using var context = _database.CreateContext();
        await IdentitySeed.UserAsync(
            context, email, password, roleName, firstName, lastName, isActive, linkedStaffId);
    }

    public async Task SeedRoleAsync(string roleName)
    {
        await using var context = _database.CreateContext();
        await IdentitySeed.RoleAsync(context, roleName);
    }

    /// <summary>
    /// Configures the salon at a zero VAT rate, the same choice
    /// <see cref="Billing.BillingHarness"/> makes: the wire tests assert what
    /// they are about — discounts, payments, stock — while the tax tests PUT
    /// the rate they need through the settings API.
    /// </summary>
    public async Task EnsureZeroTaxAsync()
    {
        await using var context = _database.CreateContext();

        if (await context.SalonSettings.AnyAsync())
        {
            return;
        }

        context.SalonSettings.Add(new SalonSettings
        {
            Id = SalonSettings.SingletonId,
            TaxRate = 0m,
            PricesIncludeTax = false
        });

        await context.SaveChangesAsync();
    }

    /// <summary>Idempotently creates the default owner account so ApiTestBase can sign in.</summary>
    public async Task EnsureDefaultUserAsync()
    {
        await using var context = _database.CreateContext();

        if (await context.Users.AnyAsync(user => user.Email == DefaultUserEmail))
        {
            return;
        }

        await IdentitySeed.UserAsync(
            context, DefaultUserEmail, DefaultUserPassword, RoleNames.Owner, "Test", "Owner");
    }

    private async Task<Guid> WithNewContextAsync(Func<DeBlissDbContext, Task<Guid>> seed)
    {
        await using var context = _database.CreateContext();
        return await seed(context);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            _database.Dispose();
        }
    }
}
