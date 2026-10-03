using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Billing;

public class InvoiceItemConfiguration : IEntityTypeConfiguration<InvoiceItem>
{
    public void Configure(EntityTypeBuilder<InvoiceItem> builder)
    {
        builder.ToTable("InvoiceItems");
        builder.HasKey(item => item.Id);

        builder.Property(item => item.Description).HasMaxLength(500).IsRequired();
        builder.Property(item => item.ItemType).HasConversion<string>().HasMaxLength(20);

        // Quantity keeps a third decimal place so fractional units (time-based or
        // weighed retail items) can be billed without a schema change.
        builder.Property(item => item.Quantity).HasPrecision(18, 3);

        // UnitPrice is what was actually charged, which may differ from both the
        // booking snapshot and the current catalogue price.
        builder.Property(item => item.UnitPrice).HasPrecision(18, 2);
        builder.Property(item => item.DiscountAmount).HasPrecision(18, 2);
        builder.Property(item => item.TaxAmount).HasPrecision(18, 2);
        builder.Property(item => item.LineTotal).HasPrecision(18, 2);

        builder.HasIndex(item => new { item.InvoiceId, item.SortOrder });

        builder.HasOne(item => item.Invoice)
            .WithMany(invoice => invoice.Items)
            .HasForeignKey(item => item.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // Restrict so a financial line can never be deleted by removing the service
        // or appointment it was derived from.
        builder.HasOne(item => item.AppointmentService)
            .WithMany()
            .HasForeignKey(item => item.AppointmentServiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(item => item.Service)
            .WithMany()
            .HasForeignKey(item => item.ServiceId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
