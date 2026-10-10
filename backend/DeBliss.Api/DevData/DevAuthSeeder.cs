using DeBliss.Application.Identity;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Staffing;
using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.Api.DevData;

/// <summary>
/// Development-only sign-ins: the three platform roles and one user per role so
/// the login flow and the role-based navigation can be exercised end to end.
///
/// Guarded twice — Program.cs only calls it in Development, and the seeder
/// re-checks — so the seed credentials can never be silently created in a
/// production database. The passwords are documented in the README and are
/// development-only; they are hashed with the same PasswordHasher the login
/// path verifies against, never stored plaintext.
/// </summary>
public static class DevAuthSeeder
{
    public const string OwnerEmail = "owner@debliss.dev";
    public const string ReceptionistEmail = "receptionist@debliss.dev";
    public const string StaffEmail = "staff@debliss.dev";

    public const string OwnerPassword = "DeBliss#Owner2026!";
    public const string ReceptionistPassword = "DeBliss#Front2026!";
    public const string StaffPassword = "DeBliss#Team2026!";

    public static async Task SeedAsync(WebApplication app)
    {
        if (!app.Environment.IsDevelopment())
        {
            return;
        }

        using var scope = app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<DeBlissDbContext>();

        var roles = await db.Roles.ToListAsync();
        var ownerRole = await EnsureRoleAsync(db, roles, RoleNames.Owner, "Salon owner / administrator — full access.");
        var receptionistRole = await EnsureRoleAsync(db, roles, RoleNames.Receptionist, "Front desk — daily operations.");
        var staffRole = await EnsureRoleAsync(db, roles, RoleNames.Staff, "Service provider — limited workspace.");
        // Roles are ensured before the user early-return so an existing
        // Development database picks up roles added later — including CUSTOMER,
        // which public registration requires.
        await EnsureRoleAsync(db, roles, RoleNames.Customer, "Customer — self-service website access.");

        if (await db.Users.AnyAsync())
        {
            return;
        }

        var now = DateTimeOffset.Now;

        var owner = DevUser(OwnerEmail, "DeBliss", "Owner", OwnerPassword, now);
        owner.UserRoles.Add(new UserRole { Role = ownerRole, AssignedAt = now });
        db.Users.Add(owner);

        var receptionist = DevUser(ReceptionistEmail, "Rita", "Desk", ReceptionistPassword, now);
        receptionist.UserRoles.Add(new UserRole { Role = receptionistRole, AssignedAt = now });
        db.Users.Add(receptionist);

        // The staff login doubles as Aisha Al Mansouri's account when the demo
        // scenario exists, so the role can be exercised against a real staff row.
        // Demo staff rows carry no email, so match on her name.
        var staffLogin = DevUser(StaffEmail, "Aisha", "Al Mansouri", StaffPassword, now);
        staffLogin.UserRoles.Add(new UserRole { Role = staffRole, AssignedAt = now });
        var aisha = await db.Staff.FirstOrDefaultAsync(staff =>
            staff.FirstName == "Aisha" && staff.LastName == "Al Mansouri");
        if (aisha is not null)
        {
            aisha.User = staffLogin;
        }
        else
        {
            db.Users.Add(staffLogin);
        }

        await db.SaveChangesAsync();
    }

    private static async Task<Role> EnsureRoleAsync(
        DeBlissDbContext db,
        List<Role> tracked,
        string name,
        string description)
    {
        var existing = tracked.FirstOrDefault(role => role.Name == name);

        if (existing is not null)
        {
            return existing;
        }

        var role = new Role { Name = name, Description = description };
        db.Roles.Add(role);
        await db.SaveChangesAsync();
        tracked.Add(role);

        return role;
    }

    private static User DevUser(
        string email,
        string firstName,
        string lastName,
        string password,
        DateTimeOffset now) =>
        new()
        {
            Email = email,
            FirstName = firstName,
            LastName = lastName,
            PasswordHash = AuthService.HashPassword(password),
            IsActive = true,
            CreatedAt = now,
        };
}
