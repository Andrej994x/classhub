using ClassHub.Core.Modules.Usage.Contracts;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Usage.Domain;

/// <summary>
/// How often a workspace did one thing on one UTC day (R3-P6): opened a tool, or created or reused an activity on a
/// board. A counter only: no user, student, visitor or time of day.
/// </summary>
public sealed class UsageCounter : ITenantOwned
{
    public const int SubjectMaxLength = 40;

    public Guid WorkspaceId { get; set; }

    public DateOnly Day { get; set; }

    public UsageEvent Event { get; set; }

    /// <summary>The tool's code (tool opened) or the board (activities).</summary>
    public required string Subject { get; set; }

    public int Count { get; set; }
}
