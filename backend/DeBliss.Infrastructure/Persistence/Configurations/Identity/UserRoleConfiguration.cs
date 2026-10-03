using DeBliss.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Identity;

public class UserRoleConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("UserRoles");
        builder.HasKey(userRole => userRole.Id);

        // The pair is unique even though it is not the key, so a role cannot be
        // assigned to the same user twice.
        builder.HasIndex(userRole => new { userRole.UserId, userRole.RoleId }).IsUnique();
    }
}
