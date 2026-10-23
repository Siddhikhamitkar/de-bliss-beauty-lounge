using DeBliss.Domain.Loyalty;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Loyalty;

public sealed class CustomerLoyaltyTransactionConfiguration : IEntityTypeConfiguration<CustomerLoyaltyTransaction>
{
    public void Configure(EntityTypeBuilder<CustomerLoyaltyTransaction> builder)
    {
        builder.ToTable("CustomerLoyaltyTransactions", table =>
        {
            table.HasCheckConstraint("CK_CustomerLoyaltyTransactions_PointsAndType",
                "([Type] = 'Earn' AND CAST([Points] AS decimal(18,2)) > 0) OR ([Type] = 'Redeem' AND CAST([Points] AS decimal(18,2)) < 0) OR ([Type] = 'Reversal' AND CAST([Points] AS decimal(18,2)) <> 0)");
            table.HasCheckConstraint("CK_CustomerLoyaltyTransactions_EligibleSpend", "CAST([EligibleSpend] AS decimal(18,2)) >= 0");
            table.HasCheckConstraint("CK_CustomerLoyaltyTransactions_EarningPercentage", "CAST([EarningPercentage] AS decimal(5,2)) BETWEEN 0 AND 100");
            table.HasCheckConstraint("CK_CustomerLoyaltyTransactions_PointValueAed", "CAST([PointValueAed] AS decimal(18,4)) BETWEEN 0.0001 AND 1000");
        });
        builder.HasKey(transaction => transaction.Id);
        builder.Property(transaction => transaction.Id).ValueGeneratedNever();
        builder.Property(transaction => transaction.Type).HasConversion<string>().HasMaxLength(16);
        builder.Property(transaction => transaction.Points).HasPrecision(18, 2);
        builder.Property(transaction => transaction.EligibleSpend).HasPrecision(18, 2);
        builder.Property(transaction => transaction.EarningPercentage).HasPrecision(5, 2);
        builder.Property(transaction => transaction.PointValueAed).HasPrecision(18, 4);
        builder.Property(transaction => transaction.Description).HasMaxLength(500).IsRequired();
        builder.Property(transaction => transaction.IdempotencyKey).HasMaxLength(100).IsRequired();
        builder.Property(transaction => transaction.OccurredAt).HasConversion(
            value => value.UtcDateTime,
            value => new DateTimeOffset(DateTime.SpecifyKind(value, DateTimeKind.Utc)));

        builder.HasIndex(transaction => transaction.IdempotencyKey).IsUnique();
        builder.HasIndex(transaction => new { transaction.InvoiceId, transaction.Type })
            .IsUnique().HasFilter("[Type] IN ('Earn','Redeem')");
        builder.HasIndex(transaction => new { transaction.CustomerId, transaction.OccurredAt });
        builder.HasOne(transaction => transaction.Customer).WithMany()
            .HasForeignKey(transaction => transaction.CustomerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.Invoice).WithMany()
            .HasForeignKey(transaction => transaction.InvoiceId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.CreatedByUser).WithMany()
            .HasForeignKey(transaction => transaction.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(transaction => transaction.ReversesTransaction).WithMany()
            .HasForeignKey(transaction => transaction.ReversesTransactionId).OnDelete(DeleteBehavior.Restrict);

        foreach (var property in builder.Metadata.GetProperties())
            property.SetAfterSaveBehavior(PropertySaveBehavior.Throw);
    }
}
