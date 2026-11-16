namespace DeBliss.Domain.Customers;

/// <summary>
/// Whether the customer can sign in to the customer app. A record created at
/// the front desk (or by import) exists for bookings and history straight away,
/// but stays <see cref="PendingActivation"/> until the owner of the mobile number
/// proves it with a one-time code and chooses a password.
/// </summary>
public enum CustomerAccountStatus
{
    PendingActivation = 0,
    Active = 1
}
