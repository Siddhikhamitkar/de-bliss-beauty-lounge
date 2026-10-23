using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Loyalty;

public sealed class LoyaltySettings : AuditableEntity
{
    public static readonly Guid SingletonId = Guid.Parse("98e51b32-1176-4f21-a7b4-e379516d2038");

    public LoyaltySettings() => Id = SingletonId;

    public bool IsEnabled { get; set; }
    public decimal EarningPercentage { get; set; } = 5m;
    public decimal PointValueAed { get; set; } = 1m;
    public decimal MinimumRedemptionPoints { get; set; }
    public decimal MaximumRedemptionPercentage { get; set; } = 50m;
    public bool AllowPartialRedemption { get; set; } = true;
    public decimal? MinimumInvoiceAmount { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public User? UpdatedByUser { get; set; }
    public Guid Version { get; set; } = Guid.NewGuid();
}
