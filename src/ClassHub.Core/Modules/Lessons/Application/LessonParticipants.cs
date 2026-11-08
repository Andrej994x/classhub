using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.SharedKernel.Errors;

namespace ClassHub.Core.Modules.Lessons.Application;

/// <summary>Who takes part in a lesson: the (active) student, or the group's current active members.</summary>
internal sealed class LessonParticipants(IStudentLookup students, IGroupLookup groups)
{
    public enum Problem
    {
        None,
        UnknownStudent,
        UnknownGroup,
        ArchivedStudent,
        ArchivedGroup,
        EmptyGroup,
    }

    public async Task<(IReadOnlyList<Guid> Ids, Problem Problem)> ResolveAsync(LessonKind kind, Guid? studentId, Guid? groupId, CancellationToken cancellationToken)
    {
        if (kind == LessonKind.Group)
        {
            var group = (await groups.FindAsync([groupId!.Value], cancellationToken)).GetValueOrDefault(groupId!.Value);
            return group is null ? ([], Problem.UnknownGroup)
                : group.Archived ? ([], Problem.ArchivedGroup)
                : group.ActiveStudentIds.Count == 0 ? ([], Problem.EmptyGroup)
                : (group.ActiveStudentIds, Problem.None);
        }

        var student = (await students.FindAsync([studentId!.Value], cancellationToken)).GetValueOrDefault(studentId!.Value);
        return student is null ? ([], Problem.UnknownStudent)
            : student.Archived ? ([], Problem.ArchivedStudent)
            : ([studentId!.Value], Problem.None);
    }

    /// <summary>For requests: the participants, or the matching API error.</summary>
    public async Task<IReadOnlyList<Guid>> RequireAsync(LessonKind kind, Guid? studentId, Guid? groupId, CancellationToken cancellationToken)
    {
        var (ids, problem) = await ResolveAsync(kind, studentId, groupId, cancellationToken);
        return problem switch
        {
            Problem.None => ids,
            Problem.UnknownStudent => throw Invalid("studentId", "Unknown student."),
            Problem.UnknownGroup => throw Invalid("groupId", "Unknown group."),
            Problem.ArchivedStudent => throw new BusinessRuleException("Restore the student before scheduling lessons.", ErrorCodes.InvalidStatusTransition),
            Problem.ArchivedGroup => throw new BusinessRuleException("Restore the group before scheduling lessons for it.", ErrorCodes.InvalidStatusTransition),
            _ => throw new BusinessRuleException("The group has no students yet."),
        };
    }

    private static RequestValidationException Invalid(string field, string message) =>
        new(new Dictionary<string, string[]>(StringComparer.Ordinal) { [field] = [message] });
}
