using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public class StaffTimeOffConfiguration : IEntityTypeConfiguration<StaffTimeOff>
{
    public void Configure(EntityTypeBuilder<StaffTimeOff> builder)
    {
        builder.ToTable("StaffTimeOff");
        builder.HasKey(timeOff => timeOff.Id);

        builder.Property(timeOff => timeOff.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(timeOff => timeOff.Reason).HasMaxLength(500);

        // Supports the availability calculation for a date range: working hours
        // minus time off minus blocked time.
        builder.HasIndex(timeOff => new { timeOff.StaffId, timeOff.StartsAt, timeOff.EndsAt });

        builder.HasOne(timeOff => timeOff.Staff)
            .WithMany(staff => staff.TimeOff)
            .HasForeignKey(timeOff => timeOff.StaffId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
