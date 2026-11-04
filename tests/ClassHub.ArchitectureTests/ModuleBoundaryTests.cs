using ClassHub.Core;
using ClassHub.Core.Modules;
using NetArchTest.Rules;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// Module boundaries of the modular monolith: a module may use another module only through that
/// module's <c>Contracts</c> namespace; domain code stays free of web and persistence frameworks.
/// </summary>
public sealed class ModuleBoundaryTests
{
    private const string ModulesNamespace = "ClassHub.Core.Modules.";

    public static TheoryData<string> ModuleNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in DiscoverModuleNames())
        {
            data.Add(name);
        }

        return data;
    }

    [Fact]
    public void At_least_one_module_is_discovered()
    {
        DiscoverModuleNames().ShouldNotBeEmpty();
        ModuleRegistration.Discover(CoreAssembly.Assembly).ShouldNotBeEmpty();
    }

    [Theory]
    [MemberData(nameof(ModuleNames))]
    public void Module_uses_other_modules_only_through_their_contracts(string module)
    {
        var forbidden = CoreAssembly.Assembly.GetTypes()
            .Where(t => t.Namespace is not null && t.Namespace.StartsWith(ModulesNamespace, StringComparison.Ordinal))
            .Where(t => ModuleOf(t.Namespace!) is { } other && other != module)
            .Where(t => !t.Namespace!.Contains(".Contracts", StringComparison.Ordinal))
            .Select(t => t.FullName!)
            .ToArray();

        if (forbidden.Length == 0)
        {
            return;
        }

        var result = Types.InAssembly(CoreAssembly.Assembly)
            .That().ResideInNamespace(ModulesNamespace + module)
            .ShouldNot().HaveDependencyOnAny(forbidden)
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringTests.FailingTypes(result));
    }

    [Fact]
    public void Domain_code_does_not_depend_on_web_or_persistence_frameworks()
    {
        var result = Types.InAssembly(CoreAssembly.Assembly)
            .That().ResideInNamespaceMatching(@"^ClassHub\.Core\.Modules\.\w+\.Domain")
            .ShouldNot().HaveDependencyOnAny(
                "Microsoft.AspNetCore.Http",
                "Microsoft.AspNetCore.Routing",
                "Microsoft.EntityFrameworkCore",
                "ClassHub.Core.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringTests.FailingTypes(result));
    }

    [Fact]
    public void Shared_kernel_does_not_depend_on_modules_or_infrastructure()
    {
        var result = Types.InAssembly(CoreAssembly.Assembly)
            .That().ResideInNamespace("ClassHub.Core.SharedKernel")
            .ShouldNot().HaveDependencyOnAny("ClassHub.Core.Modules", "ClassHub.Core.Infrastructure")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(LayeringTests.FailingTypes(result));
    }

    private static List<string> DiscoverModuleNames() =>
        CoreAssembly.Assembly.GetTypes()
            .Select(t => t.Namespace is null ? null : ModuleOf(t.Namespace))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

    private static string? ModuleOf(string ns)
    {
        if (!ns.StartsWith(ModulesNamespace, StringComparison.Ordinal))
        {
            return null;
        }

        var rest = ns[ModulesNamespace.Length..];
        var dot = rest.IndexOf('.', StringComparison.Ordinal);
        return dot < 0 ? rest : rest[..dot];
    }
}
