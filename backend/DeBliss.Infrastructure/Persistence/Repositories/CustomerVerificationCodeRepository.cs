using DeBliss.Application.Identity.Ports;
using DeBliss.Domain.Customers;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

public sealed class CustomerVerificationCodeRepository(DeBlissDbContext context) : ICustomerVerificationCodeRepository
{
    public async Task<int> CountCreatedSinceAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        DateTimeOffset since,
        CancellationToken cancellationToken)
    {
        // Compared client-side for the same SQLite reason as below.
        var createdAt = await context.CustomerVerificationCodes
            .AsNoTracking()
            .Where(code => code.PhoneNumber == phoneNumber && code.Purpose == purpose)
            .Select(code => code.CreatedAt)
            .ToListAsync(cancellationToken);

        return createdAt.Count(at => at >= since);
    }

    public async Task<DateTimeOffset?> GetLatestCreatedAtAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        // Ordered client-side: SQLite (tests) cannot ORDER BY a DateTimeOffset,
        // and a number only ever has a handful of rows in the last hour.
        var createdAt = await context.CustomerVerificationCodes
            .AsNoTracking()
            .Where(code => code.PhoneNumber == phoneNumber && code.Purpose == purpose)
            .Select(code => code.CreatedAt)
            .ToListAsync(cancellationToken);

        return createdAt.Count == 0 ? null : createdAt.Max();
    }

    public async Task<CustomerVerificationCode?> GetLatestPendingAsync(
        string phoneNumber,
        CustomerVerificationPurpose purpose,
        CancellationToken cancellationToken)
    {
        var codes = await context.CustomerVerificationCodes
            .Where(code => code.PhoneNumber == phoneNumber && code.Purpose == purpose)
            .ToListAsync(cancellationToken);

        var latest = codes.MaxBy(code => code.CreatedAt);

        return latest is { VerifiedAt: null, ConsumedAt: null } ? latest : null;
    }

    public Task<CustomerVerificationCode?> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken)
    {
        return context.CustomerVerificationCodes.FirstOrDefaultAsync(
            code => code.TokenHash == tokenHash && code.ConsumedAt == null,
            cancellationToken);
    }

    public void Add(CustomerVerificationCode code)
    {
        context.CustomerVerificationCodes.Add(code);
    }
}
