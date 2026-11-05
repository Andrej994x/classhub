using System.Reflection;
using ClassHub.Core;
using ClassHub.Core.Modules.Leads.Contracts;
using ClassHub.Core.Modules.Sites.Contracts;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// R1-P6 security rule: correct answers never appear in any public projection. Every type an anonymous visitor can
/// receive (named <c>Public*</c>, the site render model, the lead form, and everything reachable from them through
/// properties) is checked for answer-revealing members and for the test entities themselves.
/// </summary>
public sealed class PublicProjectionTests
{
    private static readonly Assembly Core = typeof(CoreAssembly).Assembly;

    private static IEnumerable<Type> Roots() =>
        Core.GetTypes()
            .Where(t => t is { IsPublic: true } && (t.Name.StartsWith("Public", StringComparison.Ordinal) || t.Name.StartsWith("Published", StringComparison.Ordinal)))
            .Concat([typeof(SiteRenderModel), typeof(LeadFormResponse)])
            .Distinct();

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            if (!seen.Add(type))
            {
                continue;
            }

            yield return type;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var candidate in Unwrap(property.PropertyType).Where(c => c.Assembly == Core))
                {
                    queue.Enqueue(candidate);
                }
            }
        }
    }

    private static IEnumerable<Type> Unwrap(Type type)
    {
        if (type.IsArray)
        {
            return Unwrap(type.GetElementType()!);
        }

        return type.IsGenericType ? type.GetGenericArguments().SelectMany(Unwrap).Append(type) : [type];
    }

    [Fact]
    public void Public_projections_exist()
    {
        Roots().Count().ShouldBeGreaterThan(3);
    }

    [Fact]
    public void No_public_projection_reveals_correct_answers()
    {
        var violations = Roots()
            .SelectMany(root => Reachable(root).SelectMany(type => type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.Contains("Correct", StringComparison.OrdinalIgnoreCase) || p.Name.Contains("Answer", StringComparison.OrdinalIgnoreCase))
                .Select(p => $"{root.Name} → {type.Name}.{p.Name}")))
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }

    [Fact]
    public void No_public_projection_contains_test_entities()
    {
        var violations = Roots()
            .SelectMany(root => Reachable(root)
                .Where(t => t.Namespace?.EndsWith(".Domain", StringComparison.Ordinal) == true)
                .Select(t => $"{root.Name} → {t.FullName}"))
            .Distinct()
            .ToList();

        violations.ShouldBeEmpty();
    }
}
