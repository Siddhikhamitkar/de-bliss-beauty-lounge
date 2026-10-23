using DeBliss.Domain.Billing;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;

namespace DeBliss.Domain.Loyalty;

public enum LoyaltyTransactionType
{
    Earn = 1,
    Redeem = 2,
    Reversal = 3
}

/// <summary>An immutable ledger entry; corrections require a separate reversal.</summary>
public sealed class CustomerLoyaltyTransaction
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public LoyaltyTransactionType Type { get; init; }
    public Guid CustomerId { get; init; }
    public Customer Customer { get; init; } = null!;
    public Guid InvoiceId { get; init; }
    public Invoice Invoice { get; init; } = null!;
    public decimal Points { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string Description { get; init; } = null!;
    public decimal EligibleSpend { get; init; }
    public decimal EarningPercentage { get; init; }
    public decimal PointValueAed { get; init; }
    public Guid? CreatedByUserId { get; init; }
    public User? CreatedByUser { get; init; }
    public string IdempotencyKey { get; init; } = null!;
    public Guid? ReversesTransactionId { get; init; }
    public CustomerLoyaltyTransaction? ReversesTransaction { get; init; }
}
