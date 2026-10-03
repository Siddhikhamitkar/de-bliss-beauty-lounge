using DeBliss.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DeBliss.UnitTests.Persistence;

/// <summary>
/// Builds the EF Core model once for the whole test run. Building a model never
/// opens a connection, so these tests do not need SQL Server to be running.
/// </summary>
internal static class TestModel
{
    public static readonly IModel Model = Build();

    private static IModel Build()
    {
        var options = new DbContextOptionsBuilder<DeBlissDbContext>()
            .UseSqlServer("Server=(local);Database=DeBlissModelTests;Integrated Security=true;TrustServerCertificate=true")
            .Options;

        using var context = new DeBlissDbContext(options);
        return context.Model;
    }

    public static IEntityType Entity(Type clrType) =>
        Model.FindEntityType(clrType)
        ?? throw new InvalidOperationException($"{clrType.Name} is not part of the EF Core model.");
}
