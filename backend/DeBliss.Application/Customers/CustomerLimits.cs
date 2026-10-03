namespace DeBliss.Application.Customers;

/// <summary>
/// The limits the customers module enforces. They mirror the column lengths in
/// the EF Core configurations so an over-long value is rejected with a clear 400
/// instead of failing inside SQL Server.
/// </summary>
public static class CustomerLimits
{
    public const int NameMaxLength = 100;
    public const int EmailMaxLength = 256;
    public const int PhoneMaxLength = 30;
    public const int GenderMaxLength = 20;
    public const int AddressMaxLength = 500;
    public const int NoteMaxLength = 2000;

    /// <summary>Shortest phone number (after formatting is stripped) the salon will accept.</summary>
    public const int MinNormalizedPhoneDigits = 5;
}
