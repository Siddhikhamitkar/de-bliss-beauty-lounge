using DeBliss.Domain.Inventory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DeBliss.Infrastructure.Persistence.Configurations.Inventory;

public class StockMovementConfiguration : IEntityTypeConfiguration<StockMovement>
{
    public void Configure(EntityTypeBuilder<StockMovement> builder)
    {
        builder.ToTable("StockMovements");
        builder.HasKey(movement => movement.Id);

        builder.Property(movement => movement.MovementType).HasConversion<string>().HasMaxLength(20);
        builder.Property(movement => movement.QuantityChange).HasPrecision(18, 3);
        builder.Property(movement => movement.StockBefore).HasPrecision(18, 3);
        builder.Property(movement => movement.StockAfter).HasPrecision(18, 3);
        builder.Property(movement => movement.Reference).HasMaxLength(100);
        builder.Property(movement => movement.Notes).HasMaxLength(500);

        // The history a product drawer shows, newest first.
        builder.HasIndex(movement => new { movement.ProductId, movement.OccurredAt });

        // One movement per (invoice, product, type): a retried or replayed sale
        // can never deduct the same units off the same invoice twice, and a
        // refunded sale can be put back exactly once. Rows with no invoice are
        // unlimited — both providers treat NULLs as distinct.
        builder.HasIndex(movement => new { movement.InvoiceId, movement.ProductId, movement.MovementType })
            .IsUnique();

        // A movement can be reversed once, ever: the unique index over the
        // nullable pointer is the database-level guarantee (NULLs are distinct).
        builder.HasIndex(movement => movement.ReversalOfMovementId).IsUnique();

        builder.HasOne(movement => movement.Product)
            .WithMany()
            .HasForeignKey(movement => movement.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        // Restrict so stock history is never destroyed by removing the invoice
        // it belongs to, mirroring the other financial line references.
        builder.HasOne(movement => movement.Invoice)
            .WithMany()
            .HasForeignKey(movement => movement.InvoiceId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(movement => movement.PerformedByUser)
            .WithMany()
            .HasForeignKey(movement => movement.PerformedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // The reversal pointer targets a sibling movement of the same product.
        builder.HasOne(movement => movement.ReversalOf)
            .WithMany()
            .HasForeignKey(movement => movement.ReversalOfMovementId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
