namespace DeBliss.Domain.Billing;

public enum InvoiceStatus
{
    Draft = 0,
    Issued = 1,
    PartiallyPaid = 2,
    Paid = 3,
    Void = 4,
    Refunded = 5
}

public enum InvoiceItemType
{
    Service = 0,
    Product = 1,
    Deposit = 2,
    Discount = 3,
    Adjustment = 4,
    Tax = 5,
    Other = 99
}

/// <summary>
/// GiftCard, Voucher, PackageCredit and LoyaltyPoints are the extension points for
/// future package redemption and loyalty without redesigning billing.
/// </summary>
public enum PaymentMethod
{
    Cash = 0,
    Card = 1,
    BankTransfer = 2,
    MobileWallet = 3,
    GiftCard = 4,
    Voucher = 5,
    PackageCredit = 6,
    LoyaltyPoints = 7,
    Other = 99
}

public enum PaymentStatus
{
    Pending = 0,
    Completed = 1,
    Failed = 2,
    Refunded = 3,
    Voided = 4
}
