using DeBliss.Domain.Common;

namespace DeBliss.Domain.Services;

/// <summary>
/// One component of a bundle: a service at a position in the delivery
/// sequence. <see cref="Quantity"/> exists for future weighed/fractional
/// cases; V1 always books exactly one of each component, and the unique
/// constraint on (ServiceBundleId, ServiceId) forbids accidental duplicates.
/// </summary>
public class ServiceBundleItem : AuditableEntity
{
    public Guid ServiceBundleId { get; set; }

    public ServiceBundle ServiceBundle { get; set; } = null!;

    public Guid ServiceId { get; set; }

    public Service Service { get; set; } = null!;

    /// <summary>The position in the delivery sequence; lower runs first.</summary>
    public int DisplayOrder { get; set; }

    public int Quantity { get; set; } = 1;
}
