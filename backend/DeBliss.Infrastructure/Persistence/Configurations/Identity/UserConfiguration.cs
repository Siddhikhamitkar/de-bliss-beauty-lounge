using DeBliss.Domain.Identity;
using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Identity;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(user => user.Id);

        builder.Property(user => user.Email).HasMaxLength(256).IsRequired();
        builder.Property(user => user.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.LastName).HasMaxLength(100).IsRequired();
        builder.Property(user => user.PhoneNumber).HasMaxLength(30);
        builder.Property(user => user.PasswordHash).HasMaxLength(500);

        builder.HasIndex(user => user.Email).IsUnique();

        builder.HasMany(user => user.UserRoles)
            .WithOne(userRole => userRole.User)
            .HasForeignKey(userRole => userRole.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict: deleting a login must never silently remove the staff record
        // that appears on historical appointments.
        builder.HasOne(user => user.StaffProfile)
            .WithOne(staff => staff.User)
            .HasForeignKey<Staff>(staff => staff.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
