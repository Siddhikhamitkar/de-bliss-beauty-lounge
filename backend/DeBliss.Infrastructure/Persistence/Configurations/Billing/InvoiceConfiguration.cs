using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Billing;

public class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("Invoices");
        builder.HasKey(invoice => invoice.Id);

        builder.Property(invoice => invoice.InvoiceNumber).HasMaxLength(50).IsRequired();
        builder.Property(invoice => invoice.Currency).HasMaxLength(3).IsRequired();
        builder.Property(invoice => invoice.Notes).HasMaxLength(2000);

        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(invoice => invoice.SubTotal).HasPrecision(18, 2);
        builder.Property(invoice => invoice.DiscountTotal).HasPrecision(18, 2);
        builder.Property(invoice => invoice.TaxTotal).HasPrecision(18, 2);
        builder.Property(invoice => invoice.TotalAmount).HasPrecision(18, 2);

        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => invoice.CustomerId);
        builder.HasIndex(invoice => invoice.Status);

        // Deliberately not unique. One invoice per checkout is an application-layer
        // convention; the schema still allows a deposit invoice and a final invoice
        // for the same appointment if the salon ever needs it.
        builder.HasIndex(invoice => invoice.AppointmentId);

        // Restrict: financial records are never physically deleted, and deleting an
        // appointment or customer must not take an invoice with it.
        builder.HasOne(invoice => invoice.Appointment)
            .WithMany(appointment => appointment.Invoices)
            .HasForeignKey(invoice => invoice.AppointmentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(invoice => invoice.Customer)
            .WithMany()
            .HasForeignKey(invoice => invoice.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(invoice => invoice.CreatedByUser)
            .WithMany()
            .HasForeignKey(invoice => invoice.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
