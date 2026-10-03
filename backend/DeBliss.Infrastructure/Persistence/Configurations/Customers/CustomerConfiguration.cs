using DeBliss.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Customers;

public class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("Customers");
        builder.HasKey(customer => customer.Id);

        builder.Property(customer => customer.FirstName).HasMaxLength(100).IsRequired();
        builder.Property(customer => customer.LastName).HasMaxLength(100).IsRequired();
        builder.Property(customer => customer.Email).HasMaxLength(256);
        builder.Property(customer => customer.PhoneNumber).HasMaxLength(30);
        builder.Property(customer => customer.Gender).HasMaxLength(20);
        builder.Property(customer => customer.Address).HasMaxLength(500);

        // Not unique: duplicates are a real-world data problem the front desk has
        // to resolve, and a hard constraint would block booking a returning guest.
        builder.HasIndex(customer => customer.Email);
        builder.HasIndex(customer => customer.PhoneNumber);
        builder.HasIndex(customer => new { customer.LastName, customer.FirstName });

        builder.HasIndex(customer => customer.IsActive);

        builder.Property(customer => customer.IsActive).HasDefaultValue(true);

        builder.HasOne(customer => customer.User)
            .WithMany()
            .HasForeignKey(customer => customer.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
