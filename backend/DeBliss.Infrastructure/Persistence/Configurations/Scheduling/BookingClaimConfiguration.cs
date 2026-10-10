using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Scheduling;

public class BookingClaimConfiguration : IEntityTypeConfiguration<BookingClaim>
{
    public void Configure(EntityTypeBuilder<BookingClaim> builder)
    {
        builder.ToTable("BookingClaims");
        builder.HasKey(claim => claim.Id);

        builder.Property(claim => claim.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(claim => claim.RequestHash).HasMaxLength(64).IsRequired();

        // The concurrency anchor: one claim per customer per key, enforced by
        // the database so two simultaneous submits cannot both win. And one
        // claim per appointment, so a replay maps back to exactly one booking.
        builder.HasIndex(claim => new { claim.CustomerId, claim.IdempotencyKey }).IsUnique();
        builder.HasIndex(claim => claim.AppointmentId)
            .IsUnique()
            .HasFilter("[AppointmentId] IS NOT NULL");

        builder.HasOne(claim => claim.Customer)
            .WithMany()
            .HasForeignKey(claim => claim.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(claim => claim.Appointment)
            .WithMany()
            .HasForeignKey(claim => claim.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
