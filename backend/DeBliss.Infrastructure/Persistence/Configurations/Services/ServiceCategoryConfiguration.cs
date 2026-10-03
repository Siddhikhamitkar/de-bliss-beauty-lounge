using DeBliss.Domain.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Services;

public class ServiceCategoryConfiguration : IEntityTypeConfiguration<ServiceCategory>
{
    public void Configure(EntityTypeBuilder<ServiceCategory> builder)
    {
        builder.ToTable("ServiceCategories");
        builder.HasKey(category => category.Id);

        builder.Property(category => category.Name).HasMaxLength(100).IsRequired();
        builder.Property(category => category.Description).HasMaxLength(500);

        builder.HasIndex(category => category.Name).IsUnique();
    }
}
