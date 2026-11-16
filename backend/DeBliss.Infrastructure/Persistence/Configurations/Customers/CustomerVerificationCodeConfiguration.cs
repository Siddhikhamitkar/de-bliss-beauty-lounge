using DeBliss.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Customers;

public class CustomerVerificationCodeConfiguration : IEntityTypeConfiguration<CustomerVerificationCode>
{
    public void Configure(EntityTypeBuilder<CustomerVerificationCode> builder)
    {
        builder.ToTable("CustomerVerificationCodes");
        builder.HasKey(code => code.Id);

        builder.Property(code => code.Purpose).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(code => code.PhoneNumber).HasMaxLength(MobileNumber.MaxLength).IsRequired();
        builder.Property(code => code.CodeHash).HasMaxLength(128).IsRequired();
        builder.Property(code => code.TokenHash).HasMaxLength(128);

        // Cooldown and hourly-limit checks, and "latest code for this number".
        builder.HasIndex(code => new { code.PhoneNumber, code.Purpose, code.CreatedAt });

        builder.HasIndex(code => code.TokenHash)
            .IsUnique()
            .HasFilter("[TokenHash] IS NOT NULL");

        builder.HasOne<Customer>()
            .WithMany()
            .HasForeignKey(code => code.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
