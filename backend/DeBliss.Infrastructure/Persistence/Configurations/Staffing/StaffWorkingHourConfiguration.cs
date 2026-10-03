using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public class StaffWorkingHourConfiguration : IEntityTypeConfiguration<StaffWorkingHour>
{
    public void Configure(EntityTypeBuilder<StaffWorkingHour> builder)
    {
        builder.ToTable("StaffWorkingHours");
        builder.HasKey(workingHour => workingHour.Id);

        builder.Property(workingHour => workingHour.DayOfWeek).HasConversion<string>().HasMaxLength(20);

        // StartTime is included in the key so a split shift (morning and evening
        // on the same weekday) can be stored as two rows.
        builder.HasIndex(workingHour => new
        {
            workingHour.StaffId,
            workingHour.DayOfWeek,
            workingHour.StartTime,
        }).IsUnique();

        builder.HasOne(workingHour => workingHour.Staff)
            .WithMany(staff => staff.WorkingHours)
            .HasForeignKey(workingHour => workingHour.StaffId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
