using DeBliss.Application.Dashboard;
using DeBliss.Application.Scheduling;
using DeBliss.Domain.Billing;
using DeBliss.Domain.Scheduling;
using DeBliss.Infrastructure.Persistence;
using DeBliss.Infrastructure.Persistence.Repositories;
using DeBliss.UnitTests.TestDoubles;

namespace DeBliss.UnitTests.Dashboard;

/// <summary>
/// Wires the real owner dashboard service — including the receptionist snapshot
/// it reuses — to the real repositories over a throwaway SQLite database. The
/// salon zone is pinned to Asia/Dubai, the production default, so the
/// day-boundary tests prove the same conversion production does; the clock is
/// fixed, so "today" is always 2026-10-03 and no test depends on the wall
/// clock. Paid invoices are written directly with chosen VAT, tip and payment
/// rows: the checkout flow itself is proven in the billing tests, these prove
/// what the dashboard does with the rows checkout leaves behind.
/// </summary>
internal sealed class OwnerDashboardHarness : IDisposable
{
    public static readonly TimeZoneInfo Salon = TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai");

    public OwnerDashboardHarness()
    {
        Database = new SqliteTestDatabase();
        Context = Database.CreateContext();

        Dashboard = new OwnerDashboardService(
            new ReceptionistDashboardService(
                new AppointmentRepository(Context),
                new StaffRepository(Context),
                new ScheduleRepository(Context),
                new DashboardReadStore(Context),
                Database.Clock),
            new OwnerDashboardReadStore(Context),
            new FixedSalonTimeZone(),
            Database.Clock);
    }

    private sealed class FixedSalonTimeZone : ISalonTimeZone
    {
        public TimeZoneInfo Zone => Salon;
    }

    public SqliteTestDatabase Database { get; }

    public DeBlissDbContext Context { get; }

    public IOwnerDashboardService Dashboard { get; }

    /// <summary>The service's "now" — fixed at 2026-10-03 09:00 UTC (13:00 Dubai).</summary>
    public DateTimeOffset Now => Database.Clock.GetUtcNow();

    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(Now, Salon).Date);

    /// <summary>A salon-local instant on the given date at the given wall-clock time.</summary>
    public DateTimeOffset Local(DateOnly date, int hour, int minute = 0)
    {
        var local = date.ToDateTime(new TimeOnly(hour, minute));
        return new DateTimeOffset(local, Salon.GetUtcOffset(local));
    }

    public sealed record Line(
        InvoiceItemType ItemType,
        string Description,
        decimal LineTotal,
        decimal Quantity = 1m,
        Guid? ServiceId = null,
        Guid? ProductId = null);

    /// <summary>A fully paid checkout at a chosen moment: the total is the tax-exclusive
    /// shape checkout writes — payable = lines − discounts + VAT + tip.</summary>
    public Task<Guid> SeedPaidInvoiceAsync(
        string invoiceNumber,
        DateTimeOffset issuedAt,
        IReadOnlyList<Line> lines,
        decimal discountTotal = 0m,
        decimal taxTotal = 0m,
        decimal tipAmount = 0m,
        DateTimeOffset? paidAt = null,
        Guid? appointmentId = null)
    {
        var subTotal = lines.Sum(line => line.LineTotal);
        var payable = subTotal - discountTotal + taxTotal + tipAmount;

        return SeedInvoiceAsync(
            invoiceNumber,
            InvoiceStatus.Paid,
            issuedAt,
            subTotal,
            discountTotal,
            taxTotal,
            tipAmount,
            payable,
            appointmentId,
            [(PaymentStatus.Completed, payable, paidAt ?? issuedAt)],
            lines);
    }

    public async Task<Guid> SeedInvoiceAsync(
        string invoiceNumber,
        InvoiceStatus status,
        DateTimeOffset? issuedAt,
        decimal subTotal = 0m,
        decimal discountTotal = 0m,
        decimal taxTotal = 0m,
        decimal tipAmount = 0m,
        decimal? totalAmount = null,
        Guid? appointmentId = null,
        (PaymentStatus Status, decimal Amount, DateTimeOffset PaidAt)[]? payments = null,
        IReadOnlyList<Line>? lines = null)
    {
        await using var context = Database.CreateContext();

        var invoice = new Invoice
        {
            Id = Guid.NewGuid(),
            InvoiceNumber = invoiceNumber,
            AppointmentId = appointmentId,
            Status = status,
            Currency = "AED",
            SubTotal = subTotal,
            DiscountTotal = discountTotal,
            TaxTotal = taxTotal,
            TipAmount = tipAmount,
            TotalAmount = totalAmount ?? subTotal,
            IssuedAt = issuedAt
        };

        foreach (var line in lines ?? [])
        {
            invoice.Items.Add(new InvoiceItem
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                ItemType = line.ItemType,
                ServiceId = line.ServiceId,
                ProductId = line.ProductId,
                Description = line.Description,
                Quantity = line.Quantity,
                LineTotal = line.LineTotal,
                UnitPrice = line.Quantity == 0m ? line.LineTotal : line.LineTotal / line.Quantity,
                SortOrder = invoice.Items.Count
            });
        }

        foreach (var payment in payments ?? [])
        {
            invoice.Payments.Add(new Payment
            {
                Id = Guid.NewGuid(),
                InvoiceId = invoice.Id,
                Amount = payment.Amount,
                Currency = "AED",
                Method = PaymentMethod.Cash,
                Status = payment.Status,
                PaidAt = payment.PaidAt
            });
        }

        context.Invoices.Add(invoice);
        await context.SaveChangesAsync();
        Context.ChangeTracker.Clear();
        return invoice.Id;
    }

    public async Task<Guid> SeedStaffAsync(string firstName = "Sara", string lastName = "Stylist")
    {
        await using var context = Database.CreateContext();
        var id = await StaffingSeed.StaffAsync(context, firstName, lastName, isActive: true);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedCategoryAsync(string name)
    {
        await using var context = Database.CreateContext();
        var id = await CatalogueSeed.CategoryAsync(context, name);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedServiceAsync(Guid categoryId, string name, decimal price = 100m)
    {
        await using var context = Database.CreateContext();
        var id = await CatalogueSeed.ServiceAsync(context, categoryId, name, price, 60);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedProductAsync(
        string name,
        decimal currentStock,
        decimal minimumStock,
        bool isActive = true)
    {
        await using var context = Database.CreateContext();
        var id = await InventorySeed.ProductAsync(
            context, name, $"SKU-{Guid.NewGuid():N}"[..12], currentStock: currentStock, minimumStock: minimumStock,
            isActive: isActive);
        Context.ChangeTracker.Clear();
        return id;
    }

    public async Task<Guid> SeedAppointmentAsync(
        DateTimeOffset startsAt,
        AppointmentStatus status,
        Guid staffId,
        Guid serviceId,
        Guid? customerId = null)
    {
        await using var context = Database.CreateContext();
        var id = await SchedulingSeed.AppointmentAsync(
            context,
            startsAt,
            startsAt.AddHours(1),
            status,
            customerId,
            lines: [new SchedulingSeed.Line(serviceId, staffId, "Seeded service")]);
        Context.ChangeTracker.Clear();
        return id;
    }

    public void Dispose()
    {
        Context.Dispose();
        Database.Dispose();
    }
}
