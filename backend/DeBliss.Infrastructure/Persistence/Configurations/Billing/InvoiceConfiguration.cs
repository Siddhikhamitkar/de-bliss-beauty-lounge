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
        builder.Property(invoice => invoice.CouponCode).HasMaxLength(50);
        builder.Property(invoice => invoice.BundleNameSnapshot).HasMaxLength(150);

        builder.Property(invoice => invoice.Status).HasConversion<string>().HasMaxLength(20);

        builder.Property(invoice => invoice.SubTotal).HasPrecision(18, 2);
        builder.Property(invoice => invoice.DiscountTotal).HasPrecision(18, 2);
        builder.Property(invoice => invoice.CouponDiscount).HasPrecision(18, 2);
        builder.Property(invoice => invoice.ManualDiscount).HasPrecision(18, 2);
        builder.Property(invoice => invoice.BundleDiscount).HasPrecision(18, 2);
        builder.Property(invoice => invoice.TaxTotal).HasPrecision(18, 2);

        // The rate and mode are snapshots of the settings on the day of
        // checkout, stored on the bill they produced.
        builder.Property(invoice => invoice.TaxRate).HasPrecision(5, 2);
        builder.Property(invoice => invoice.TipAmount).HasPrecision(18, 2);
        builder.Property(invoice => invoice.TipPercentage).HasPrecision(5, 2);

        builder.Property(invoice => invoice.TotalAmount).HasPrecision(18, 2);

        builder.HasIndex(invoice => invoice.InvoiceNumber).IsUnique();
        builder.HasIndex(invoice => invoice.CustomerId);
        builder.HasIndex(invoice => invoice.Status);
        builder.HasIndex(invoice => invoice.CouponId);

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

        // Restrict: coupons are retired by deactivating them, never deleted, and a
        // paid invoice must never be rewritten because its coupon row changed.
        builder.HasOne(invoice => invoice.Coupon)
            .WithMany()
            .HasForeignKey(invoice => invoice.CouponId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: bundles are retired by deactivating them, never deleted, and a
        // paid invoice must never be rewritten because the bundle row changed.
        builder.HasOne(invoice => invoice.ServiceBundle)
            .WithMany(bundle => bundle.Invoices)
            .HasForeignKey(invoice => invoice.ServiceBundleId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict: tip attribution on a paid invoice is financial history and
        // must never be rewritten or removed by a staff record change.
        builder.HasOne(invoice => invoice.TipStaff)
            .WithMany()
            .HasForeignKey(invoice => invoice.TipStaffId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(invoice => invoice.CreatedByUser)
            .WithMany()
            .HasForeignKey(invoice => invoice.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
