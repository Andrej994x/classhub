using ClassHub.Core;
using NetArchTest.Rules;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// Dependency rules between the host and the module library. Module-to-module rules are added in R0-P3.
/// </summary>
public sealed class LayeringTests
{
    [Fact]
    public void Core_does_not_depend_on_the_api_host()
    {
        var result = Types.InAssembly(CoreAssembly.Assembly)
            .ShouldNot()
            .HaveDependencyOn("ClassHub.Api")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(FailingTypes(result));
    }

    internal static string FailingTypes(NetArchTest.Rules.TestResult result) =>
        "Violating types: " + string.Join(", ", result.FailingTypeNames ?? []);
}
