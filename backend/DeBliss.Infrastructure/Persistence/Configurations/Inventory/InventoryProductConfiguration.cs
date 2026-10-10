using DeBliss.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Inventory;

public class InventoryProductConfiguration : IEntityTypeConfiguration<InventoryProduct>
{
    public void Configure(EntityTypeBuilder<InventoryProduct> builder)
    {
        builder.ToTable("InventoryProducts");
        builder.HasKey(product => product.Id);

        builder.Property(product => product.Name).HasMaxLength(200).IsRequired();
        builder.Property(product => product.Brand).HasMaxLength(100);
        builder.Property(product => product.Sku).HasMaxLength(50).IsRequired();
        builder.Property(product => product.Category).HasMaxLength(100);
        builder.Property(product => product.InventoryType).HasConversion<string>().HasMaxLength(20);
        builder.Property(product => product.UnitOfMeasure).HasMaxLength(20).IsRequired();

        builder.Property(product => product.CostPrice).HasPrecision(18, 2);
        builder.Property(product => product.SellingPrice).HasPrecision(18, 2);

        // Stock keeps a third decimal place so fractional units (ml, g) can be
        // tracked without a schema change, matching invoice line quantities.
        builder.Property(product => product.CurrentStock).HasPrecision(18, 3);
        builder.Property(product => product.MinimumStock).HasPrecision(18, 3);

        // Client-maintained optimistic-concurrency token: not rowversion, so the
        // model behaves identically on SQL Server and SQLite.
        builder.Property(product => product.StockVersion).IsConcurrencyToken();

        // Unique: the application stores SKUs trimmed and upper-cased, so this
        // index is what actually guarantees a SKU cannot be created twice.
        builder.HasIndex(product => product.Sku).IsUnique();
        builder.HasIndex(product => product.InventoryType);
        builder.HasIndex(product => product.IsActive);
    }
}
