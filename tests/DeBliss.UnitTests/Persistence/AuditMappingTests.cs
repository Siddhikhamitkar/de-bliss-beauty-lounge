using DeBliss.Domain.Common;
using DeBliss.Domain.Identity;
using DeBliss.Domain.Scheduling;
using Microsoft.EntityFrameworkCore;

namespace DeBliss.UnitTests.Persistence;

public class AuditMappingTests
{
    public static IEnumerable<object[]> AuditableEntities() =>
        typeof(Entity).Assembly
            .GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && typeof(AuditableEntity).IsAssignableFrom(type))
            .Select(type => new object[] { type });

    [Theory]
    [MemberData(nameof(AuditableEntities))]
    public void Auditable_entities_carry_both_timestamp_columns(Type entityType)
    {
        var entity = TestModel.Entity(entityType);

        Assert.Equal(typeof(DateTimeOffset), entity.FindProperty(nameof(AuditableEntity.CreatedAt))!.ClrType);
        Assert.True(entity.FindProperty(nameof(AuditableEntity.UpdatedAt))!.IsNullable);
    }

    [Fact]
    public void Only_the_rows_whose_timestamps_are_domain_facts_are_excluded_from_auditing()
    {
        // UserRole.AssignedAt and AppointmentStatusHistory.ChangedAt describe when
        // something happened in the salon, not when the row was written, so the
        // application sets them and the audit interceptor must leave them alone.
        var nonAuditable = TestModel.Model.GetEntityTypes()
            .Select(entity => entity.ClrType)
            .Where(type => !typeof(AuditableEntity).IsAssignableFrom(type))
            .OrderBy(type => type.Name)
            .ToArray();

        Assert.Equal([typeof(AppointmentStatusHistory), typeof(UserRole)], nonAuditable);
    }
}
