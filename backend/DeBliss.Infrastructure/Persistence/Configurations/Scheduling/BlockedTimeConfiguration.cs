using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Scheduling;

public class BlockedTimeConfiguration : IEntityTypeConfiguration<BlockedTime>
{
    public void Configure(EntityTypeBuilder<BlockedTime> builder)
    {
        builder.ToTable("BlockedTime");
        builder.HasKey(blockedTime => blockedTime.Id);

        builder.Property(blockedTime => blockedTime.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(blockedTime => blockedTime.Reason).HasMaxLength(500);

        // StaffId is nullable: a null value blocks the whole salon rather than one
        // staff member's column.
        builder.HasIndex(blockedTime => new
        {
            blockedTime.StaffId,
            blockedTime.StartsAt,
            blockedTime.EndsAt,
        });
        builder.HasIndex(blockedTime => new { blockedTime.StartsAt, blockedTime.EndsAt });

        builder.HasOne(blockedTime => blockedTime.Staff)
            .WithMany(staff => staff.BlockedTimes)
            .HasForeignKey(blockedTime => blockedTime.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(blockedTime => blockedTime.CreatedByUser)
            .WithMany()
            .HasForeignKey(blockedTime => blockedTime.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
