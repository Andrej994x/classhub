using ClassHub.Core;
using System.Reflection;

namespace ClassHub.ArchitectureTests;

/// <summary>
/// R2-P1: the teacher's private notes about a student travel only through the notes endpoint. No other contract type
/// of the Students module (and none of the student-facing ones that come with the portal) may carry a note.
/// </summary>
public sealed class StudentPrivacyTests
{
    private static readonly HashSet<string> NoteTypes = new(StringComparer.Ordinal) { "StudentNoteResponse", "AddStudentNoteRequest" };

    [Fact]
    public void Only_the_notes_contracts_carry_private_notes()
    {
        var contracts = CoreAssembly.Assembly.GetTypes()
            .Where(t => t.Namespace == "ClassHub.Core.Modules.Students.Contracts" && t.IsPublic && !NoteTypes.Contains(t.Name))
            .ToList();
        contracts.ShouldNotBeEmpty();

        var leaks = contracts
            .SelectMany(t => Reachable(t).SelectMany(r => r.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.Name.Contains("Note", StringComparison.OrdinalIgnoreCase) || NoteTypes.Contains(Element(p.PropertyType).Name))
                .Select(p => $"{t.Name} → {r.Name}.{p.Name}")))
            .ToList();

        leaks.ShouldBeEmpty();
    }

    private static IEnumerable<Type> Reachable(Type root)
    {
        var seen = new HashSet<Type>();
        var queue = new Queue<Type>([root]);
        while (queue.TryDequeue(out var type))
        {
            type = Element(type);
            if (!seen.Add(type) || type.Namespace?.StartsWith("ClassHub.", StringComparison.Ordinal) != true)
            {
                continue;
            }

            yield return type;
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                queue.Enqueue(property.PropertyType);
            }
        }
    }

    private static Type Element(Type type) =>
        Nullable.GetUnderlyingType(type) ?? (type.IsGenericType ? type.GetGenericArguments().LastOrDefault() ?? type : type.IsArray ? type.GetElementType()! : type);
}
