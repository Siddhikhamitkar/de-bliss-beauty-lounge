using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Billing;

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(payment => payment.Id);

        builder.Property(payment => payment.Currency).HasMaxLength(3).IsRequired();
        builder.Property(payment => payment.Reference).HasMaxLength(100);
        builder.Property(payment => payment.Notes).HasMaxLength(2000);

        builder.Property(payment => payment.Amount).HasPrecision(18, 2);

        // Method and Status are stored as readable strings; GiftCard, Voucher,
        // PackageCredit and LoyaltyPoints are the future package/loyalty tender types.
        builder.Property(payment => payment.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(payment => payment.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasIndex(payment => payment.InvoiceId);
        builder.HasIndex(payment => payment.PaidAt);

        builder.HasOne(payment => payment.Invoice)
            .WithMany(invoice => invoice.Payments)
            .HasForeignKey(payment => payment.InvoiceId)
            .OnDelete(DeleteBehavior.Cascade);

        // A refund is a new row pointing back at the original, so the original is
        // never edited or deleted. Restrict keeps that history intact.
        builder.HasOne(payment => payment.RefundOfPayment)
            .WithMany()
            .HasForeignKey(payment => payment.RefundOfPaymentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(payment => payment.ReceivedByUser)
            .WithMany()
            .HasForeignKey(payment => payment.ReceivedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
