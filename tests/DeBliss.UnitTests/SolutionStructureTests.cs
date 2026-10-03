using System.Reflection;

namespace DeBliss.UnitTests;

public class SolutionStructureTests
{
    [Theory]
    [InlineData("DeBliss.Domain")]
    [InlineData("DeBliss.Application")]
    [InlineData("DeBliss.Infrastructure")]
    [InlineData("DeBliss.Api")]
    public void Project_assembly_is_available_to_the_test_host(string assemblyName)
    {
        Assert.NotNull(Assembly.Load(assemblyName));
    }
}
