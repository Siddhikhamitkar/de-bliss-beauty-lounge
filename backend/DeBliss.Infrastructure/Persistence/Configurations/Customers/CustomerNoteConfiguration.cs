using DeBliss.Domain.Customers;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Customers;

public class CustomerNoteConfiguration : IEntityTypeConfiguration<CustomerNote>
{
    public void Configure(EntityTypeBuilder<CustomerNote> builder)
    {
        builder.ToTable("CustomerNotes");
        builder.HasKey(note => note.Id);

        builder.Property(note => note.Note).HasMaxLength(2000).IsRequired();

        builder.HasIndex(note => note.CustomerId);

        builder.HasOne(note => note.Customer)
            .WithMany(customer => customer.Notes)
            .HasForeignKey(note => note.CustomerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(note => note.CreatedByUser)
            .WithMany()
            .HasForeignKey(note => note.CreatedByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
