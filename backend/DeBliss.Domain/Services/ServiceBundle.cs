using DeBliss.Domain.Common;

namespace DeBliss.Domain.Services;

/// <summary>
/// A bundle offered on the service menu: several existing services booked
/// together as one offering at its own price. <see cref="Price"/> is the
/// bundle's catalogue price, which may be below the sum of its components —
/// the saving is derived, never stored. Duration is likewise derived from the
/// components, so the bundle window and its lines can never disagree.
/// Like services, a bundle is deactivated rather than deleted so history
/// stays resolvable.
/// </summary>
public class ServiceBundle : AuditableEntity
{
    public Guid? ServiceCategoryId { get; set; }

    public ServiceCategory? ServiceCategory { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    /// <summary>Money is always decimal, never a floating point type.</summary>
    public decimal Price { get; set; }

    public bool IsActive { get; set; } = true;

    public int SortOrder { get; set; }

    /// <summary>The sum of the component durations: the window a booking occupies.</summary>
    public int TotalDurationMinutes => Items.Sum(item => item.Service.DurationMinutes);

    /// <summary>The sum of the component prices at their current catalogue values.</summary>
    public decimal StandardTotalPrice => Items.Sum(item => item.Service.Price);

    /// <summary>The saving against booking the components individually; zero when nothing is saved.</summary>
    public decimal SavingsAmount => StandardTotalPrice - Price;

    /// <summary>
    /// A bundle can only be booked when it is active, its category is active,
    /// and every component is itself bookable. Requires the component graph to
    /// have been loaded.
    /// </summary>
    public bool CanBeBooked() =>
        IsActive
        && ServiceCategory is { IsActive: true }
        && Items.Count >= MinimumComponents
        && Items.All(item => item.Service.CanBeBooked());

    /// <summary>A bundle with fewer than two services is not an offering.</summary>
    public const int MinimumComponents = 2;

    public ICollection<ServiceBundleItem> Items { get; set; } = new List<ServiceBundleItem>();

    public ICollection<Scheduling.Appointment> Appointments { get; set; } = new List<Scheduling.Appointment>();

    public ICollection<Billing.Invoice> Invoices { get; set; } = new List<Billing.Invoice>();
}
