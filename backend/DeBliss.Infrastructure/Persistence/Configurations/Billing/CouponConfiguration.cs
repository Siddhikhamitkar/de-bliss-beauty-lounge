using DeBliss.Domain.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Billing;

public class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons");
        builder.HasKey(coupon => coupon.Id);

        builder.Property(coupon => coupon.Code).HasMaxLength(50).IsRequired();
        builder.Property(coupon => coupon.DiscountType).HasConversion<string>().HasMaxLength(20);
        builder.Property(coupon => coupon.DiscountValue).HasPrecision(18, 2);

        // Unique: the application stores codes trimmed and upper-cased, so this
        // index is what actually guarantees a code cannot be created twice.
        builder.HasIndex(coupon => coupon.Code).IsUnique();
        builder.HasIndex(coupon => coupon.IsActive);
    }
}
