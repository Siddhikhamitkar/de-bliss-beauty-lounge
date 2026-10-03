using DeBliss.Domain.Common;
using System.Reflection;

namespace DeBliss.UnitTests.Domain;

public class DomainMoneyTests
{
    [Fact]
    public void No_domain_property_uses_a_floating_point_type()
    {
        var floatingPointProperties = typeof(Entity).Assembly
            .GetTypes()
            .Where(type => type.IsClass)
            .SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.PropertyType == typeof(double)
                            || property.PropertyType == typeof(float)
                            || property.PropertyType == typeof(double?)
                            || property.PropertyType == typeof(float?))
            .Select(property => $"{property.DeclaringType!.Name}.{property.Name}")
            .ToList();

        Assert.Empty(floatingPointProperties);
    }
}
