using DeBliss.Domain.Customers;
using DeBliss.Domain.Notifications;
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
        builder.Property(customer => customer.NormalizedPhoneNumber).HasMaxLength(MobileNumber.MaxLength);
        builder.Property(customer => customer.Gender).HasMaxLength(20);

        // Existing rows and every new front-desk record start pending: only the
        // owner of the number, proven by a one-time code, turns it Active.
        builder.Property(customer => customer.AccountStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .HasDefaultValue(CustomerAccountStatus.PendingActivation);
        builder.Property(customer => customer.Address).HasMaxLength(500);

        // Not unique: duplicates are a real-world data problem the front desk has
        // to resolve, and a hard constraint would block booking a returning guest.
        builder.HasIndex(customer => customer.Email);
        builder.HasIndex(customer => customer.PhoneNumber);
        builder.HasIndex(customer => new { customer.LastName, customer.FirstName });

        // One mobile number, one customer: the identity key behind front-desk
        // find-or-create and mobile sign-in. Enforced by the database so two
        // simultaneous bookings for a new number cannot both create a record.
        // NULL (no readable number) stays legal for legacy rows.
        builder.HasIndex(customer => customer.NormalizedPhoneNumber)
            .IsUnique()
            .HasFilter("[NormalizedPhoneNumber] IS NOT NULL");

        builder.HasIndex(customer => customer.IsActive);

        builder.Property(customer => customer.IsActive).HasDefaultValue(true);

        // Existing and new customers start as Unknown, which the dispatcher
        // treats as "do not send" until consent is explicitly recorded.
        builder.Property(customer => customer.WhatsAppOptInStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .HasDefaultValue(WhatsAppOptInStatus.Unknown);

        builder.Property(customer => customer.WhatsAppOptInSource).HasMaxLength(40);

        builder.HasOne(customer => customer.User)
            .WithMany()
            .HasForeignKey(customer => customer.UserId)
            .OnDelete(DeleteBehavior.Restrict);

        // A portal login links to at most one customer record, and a customer
        // record to at most one login. The filter keeps the many rows with a
        // NULL link (most front-desk customers) legal under a unique index.
        builder.HasIndex(customer => customer.UserId)
            .IsUnique()
            .HasFilter("[UserId] IS NOT NULL");
    }
}
