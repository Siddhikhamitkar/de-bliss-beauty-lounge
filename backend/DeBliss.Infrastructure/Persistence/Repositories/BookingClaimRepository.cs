using DeBliss.Application.Scheduling.Ports;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Idempotency claims for customer bookings. The duplicate-key catch is what
/// turns a lost claim race into "someone else with this key already won",
/// which the caller replays instead of booking again.
/// </summary>
public sealed class BookingClaimRepository(DeBlissDbContext context) : IBookingClaimRepository
{
    public Task<BookingClaim?> FindAsync(Guid customerId, string idempotencyKey, CancellationToken ct = default)
    {
        return context.BookingClaims
            .AsNoTracking()
            .FirstOrDefaultAsync(claim =>
                claim.CustomerId == customerId && claim.IdempotencyKey == idempotencyKey, ct);
    }

    public async Task<BookingClaim?> TryAddAsync(BookingClaim claim, CancellationToken ct = default)
    {
        try
        {
            context.BookingClaims.Add(claim);
            await context.SaveChangesAsync(ct);

            return null;
        }
        catch (DbUpdateException ex) when (IsDuplicateClaim(ex))
        {
            // A concurrent request with the same customer + key committed
            // first. Drop the rejected row from the change tracker and hand
            // the winner back so the caller can replay its outcome.
            context.Entry(claim).State = EntityState.Detached;

            return await FindAsync(claim.CustomerId, claim.IdempotencyKey, ct);
        }
    }

    public async Task LinkAppointmentAsync(
        Guid customerId,
        string idempotencyKey,
        Guid appointmentId,
        CancellationToken ct = default)
    {
        var claim = await context.BookingClaims
            .FirstAsync(existing =>
                existing.CustomerId == customerId && existing.IdempotencyKey == idempotencyKey, ct);

        claim.AppointmentId = appointmentId;

        await context.SaveChangesAsync(ct);
    }

    private static bool IsDuplicateClaim(DbUpdateException exception) =>
        exception.InnerException is SqlException sqlException
        && sqlException.Number is 2601 or 2627;
}
