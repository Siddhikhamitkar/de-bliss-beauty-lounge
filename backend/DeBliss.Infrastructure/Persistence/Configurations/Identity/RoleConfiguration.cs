using DeBliss.Domain.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Identity;

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles");
        builder.HasKey(role => role.Id);

        builder.Property(role => role.Name).HasMaxLength(100).IsRequired();
        builder.Property(role => role.Description).HasMaxLength(256);

        builder.HasIndex(role => role.Name).IsUnique();

        builder.HasMany(role => role.UserRoles)
            .WithOne(userRole => userRole.Role)
            .HasForeignKey(userRole => userRole.RoleId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
