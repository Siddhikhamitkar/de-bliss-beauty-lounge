using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public class StaffConfiguration : IEntityTypeConfiguration<Staff>
{
    public void Configure(EntityTypeBuilder<Staff> builder)
    {
        builder.ToTable("Staff");
        builder.HasKey(staff => staff.Id);

        builder.Property(staff => staff.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(staff => staff.LastName).HasMaxLength(100).IsRequired();
        builder.Property(staff => staff.Email).HasMaxLength(256);
        builder.Property(staff => staff.PhoneNumber).HasMaxLength(30);
        builder.Property(staff => staff.JobTitle).HasMaxLength(100);
        builder.Property(staff => staff.CalendarColor).HasMaxLength(9);

        // A staff member is booked, not deleted: IsActive retires them from the
        // calendar while their historical appointments stay intact.
        builder.HasIndex(staff => staff.IsActive);
    }
}
