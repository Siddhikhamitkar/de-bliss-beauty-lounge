using DeBliss.Domain.Customers;

namespace DeBliss.Application.Identity.Ports;

public interface ICustomerVerificationCodeRepository
{
    /// <summary>Codes created for this number and purpose at or after <paramref name="since"/>.</summary>
    Task<int> CountCreatedSinceAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken);

    /// <summary>When the newest code for this number and purpose was created, if any.</summary>
    Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>
    /// The newest code for this number and purpose, tracked, when it is still
    /// waiting to be proven (not verified, not consumed). Older codes are dead
    /// as soon as a newer one is sent.
    /// </summary>
    Task<CustomerVerificationCode?> GetLatestPendingAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken);

    /// <summary>The proven, unspent code that issued this token hash, tracked.</summary>
    Task<CustomerVerificationCode?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    void Add(CustomerVerificationCode code);
}
