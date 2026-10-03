using DeBliss.Domain.Staffing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Staffing;

public class StaffServiceConfiguration : IEntityTypeConfiguration<StaffService>
{
    public void Configure(EntityTypeBuilder<StaffService> builder)
    {
        builder.ToTable("StaffServices");
        builder.HasKey(staffService => staffService.Id);

        // Expresses "a service may be offered by multiple staff members" and vice
        // versa. The pair is unique even though the key is a surrogate Guid.
        builder.HasIndex(staffService => new { staffService.StaffId, staffService.ServiceId }).IsUnique();
        builder.HasIndex(staffService => staffService.ServiceId);

        builder.HasOne(staffService => staffService.Staff)
            .WithMany(staff => staff.StaffServices)
            .HasForeignKey(staffService => staffService.StaffId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(staffService => staffService.Service)
            .WithMany(service => service.StaffServices)
            .HasForeignKey(staffService => staffService.ServiceId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
