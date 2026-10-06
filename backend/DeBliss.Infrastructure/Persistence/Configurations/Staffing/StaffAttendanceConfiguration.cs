using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public class StaffAttendanceConfiguration : IEntityTypeConfiguration<StaffAttendance>
{
    public void Configure(EntityTypeBuilder<StaffAttendance> builder)
    {
        builder.ToTable("StaffAttendance");
        builder.HasKey(attendance => attendance.Id);

        builder.Property(attendance => attendance.Status).HasConversion<string>().HasMaxLength(20);

        // One clock-in/clock-out pair per staff member per work date; also the
        // lookup key for "today's attendance" and for the owner's date filters.
        builder.HasIndex(attendance => new { attendance.StaffId, attendance.WorkDate }).IsUnique();

        builder.HasOne(attendance => attendance.Staff)
            .WithMany(staff => staff.Attendances)
            .HasForeignKey(attendance => attendance.StaffId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
