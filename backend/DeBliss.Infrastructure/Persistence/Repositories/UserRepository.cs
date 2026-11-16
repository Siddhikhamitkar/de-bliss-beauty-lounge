using DeBliss.Application.Identity.Ports;
using DeBliss.Domain.Customers;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Infrastructure.Persistence.Repositories;

/// <summary>
/// Looks sign-ins up by email. Deliberately simple queries (user, then roles, then
/// the staff link) instead of one deep projection: EF Core's SQLite provider refuses
/// to translate the combined shape, and the three round-trips are irrelevant at login
/// frequency.
/// </summary>
public sealed class UserRepository(DeBlissDbContext context) : IUserRepository
{
    public async Task<AuthUserReadModel?> FindByEmailAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();

        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(
                each => each.Email != null && each.Email.ToLower() == normalized,
                cancellationToken);

        return user is null ? null : await ToReadModelAsync(user, cancellationToken);
    }

    public async Task<AuthUserReadModel?> FindByIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await context.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(each => each.Id == userId, cancellationToken);

        return user is null ? null : await ToReadModelAsync(user, cancellationToken);
    }

    public async Task MarkLoggedInAsync(Guid userId, DateTimeOffset at, CancellationToken cancellationToken)
    {
        var user = await context.Users.FirstAsync(each => each.Id == userId, cancellationToken);
        user.LastLoginAt = at;
        await context.SaveChangesAsync(cancellationToken);
    }

    public Task<Guid?> GetRoleIdAsync(string roleName, CancellationToken cancellationToken)
    {
        return context.Roles
            .AsNoTracking()
            .Where(role => role.Name == roleName)
            .Select(role => (Guid?)role.Id)
            .FirstOrDefaultAsync(cancellationToken);
    }

    public void Add(User user)
    {
        context.Users.Add(user);
    }

    public async Task<AuthUserReadModel?> FindByVerifiedCustomerPhoneAsync(
        string canonicalPhone,
        CancellationToken cancellationToken)
    {
        var userId = await context.Customers
            .AsNoTracking()
            .Where(customer =>
                customer.NormalizedPhoneNumber == canonicalPhone
                && customer.UserId != null
                && customer.AccountStatus == CustomerAccountStatus.Active
                && customer.MobileVerifiedAt != null)
            .Select(customer => customer.UserId)
            .FirstOrDefaultAsync(cancellationToken);

        return userId is { } id ? await FindByIdAsync(id, cancellationToken) : null;
    }

    public Task<User?> GetTrackedAsync(Guid userId, CancellationToken cancellationToken)
    {
        return context.Users.FirstOrDefaultAsync(user => user.Id == userId, cancellationToken);
    }

    public Task<bool> IsEmailTakenAsync(string email, Guid? exceptUserId, CancellationToken cancellationToken)
    {
        var normalized = email.Trim().ToLowerInvariant();

        return context.Users.AnyAsync(
            user => user.Email != null
                && user.Email.ToLower() == normalized
                && (exceptUserId == null || user.Id != exceptUserId),
            cancellationToken);
    }

    public async Task<(string? SecurityStamp, bool IsActive)?> GetSessionStateAsync(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var state = await context.Users
            .AsNoTracking()
            .Where(user => user.Id == userId)
            .Select(user => new { user.SecurityStamp, user.IsActive })
            .FirstOrDefaultAsync(cancellationToken);

        return state is null ? null : (state.SecurityStamp, state.IsActive);
    }

    private async Task<AuthUserReadModel> ToReadModelAsync(
        User user,
        CancellationToken cancellationToken)
    {
        var roleNames = await context.UserRoles
            .AsNoTracking()
            .Where(userRole => userRole.UserId == user.Id)
            .Select(userRole => userRole.Role.Name)
            .ToListAsync(cancellationToken);

        var staffId = await context.Set<Staff>()
            .AsNoTracking()
            .Where(staff => staff.UserId == user.Id)
            .Select(staff => (Guid?)staff.Id)
            .FirstOrDefaultAsync(cancellationToken);

        var customerId = await context.Set<Customer>()
            .AsNoTracking()
            .Where(customer => customer.UserId == user.Id)
            .Select(customer => (Guid?)customer.Id)
            .FirstOrDefaultAsync(cancellationToken);

        return new AuthUserReadModel(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.PasswordHash,
            user.IsActive,
            roleNames,
            staffId,
            customerId,
            user.SecurityStamp);
    }
}
