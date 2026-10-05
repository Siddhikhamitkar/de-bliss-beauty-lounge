using DeBliss.Application.Identity;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.TestDoubles;

/// <summary>
/// Writes sign-in rows straight to the database — roles, users with properly
/// hashed passwords, and the staff link — so auth tests arrange state without
/// going through the login endpoint first.
/// </summary>
internal static class IdentitySeed
{
    public static async Task<Guid> UserAsync(
        DeBlissDbContext context,
        string email,
        string password,
        string roleName,
        string firstName = "Test",
        string lastName = "User",
        bool isActive = true,
        Guid? linkedStaffId = null)
    {
        var role = await context.Roles.FirstOrDefaultAsync(each => each.Name == roleName);

        if (role is null)
        {
            role = new Role { Name = roleName, Description = "Seeded by a test" };
            context.Roles.Add(role);
        }

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = AuthService.HashPassword(password),
            IsActive = isActive,
            CreatedAt = DateTimeOffset.UtcNow,
        };
        user.UserRoles.Add(new UserRole { User = user, Role = role, AssignedAt = DateTimeOffset.UtcNow });
        context.Users.Add(user);
        await context.SaveChangesAsync();

        if (linkedStaffId is { } staffId)
        {
            var staff = await context.Staff.FirstAsync(each => each.Id == staffId);
            staff.UserId = user.Id;
            await context.SaveChangesAsync();
        }

        return user.Id;
    }
}
