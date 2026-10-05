using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DeBliss.UnitTests.Persistence;

/// <summary>
/// Builds the EF Core model once for the whole test run. Building a model never
/// opens a connection, so these tests do not need SQL Server to be running.
/// </summary>
internal static class TestModel
{
    public static readonly IModel Model = Build();

    // Seed data only survives in the design-time model; the runtime model is
    // read-optimized and drops it.
    public static readonly IModel DesignTimeModel = BuildDesignTime();

    private static IModel Build()
    {
        using var context = new DeBlissDbContext(Options());
        return context.Model;
    }

    private static IModel BuildDesignTime()
    {
        using var context = new DeBlissDbContext(Options());
        return context.GetService<IDesignTimeModel>().Model;
    }

    private static DbContextOptions<DeBlissDbContext> Options() =>
        new DbContextOptionsBuilder<DeBlissDbContext>()
            .UseSqlServer("Server=(local);Database=DeBlissModelTests;Integrated Security=true;TrustServerCertificate=true")
            .Options;

    public static IEntityType Entity(Type clrType) =>
        Model.FindEntityType(clrType)
        ?? throw new InvalidOperationException($"{clrType.Name} is not part of the EF Core model.");

    public static IEntityType DesignTimeEntity(Type clrType) =>
        DesignTimeModel.FindEntityType(clrType)
        ?? throw new InvalidOperationException($"{clrType.Name} is not part of the EF Core design-time model.");
}
