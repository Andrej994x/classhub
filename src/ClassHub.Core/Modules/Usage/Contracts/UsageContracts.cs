namespace ClassHub.Core.Modules.Usage.Contracts;

/// <summary>What is counted (R3-P6).</summary>
public enum UsageEvent
{
    /// <summary>A tool opened from the toolbox (its code).</summary>
    ToolOpened = 1,

    /// <summary>A new activity saved on a board (the board).</summary>
    ActivityCreated = 2,

    /// <summary>A saved activity opened again or duplicated (the board).</summary>
    ActivityReused = 3,
}

/// <summary>Counts uses in the current workspace; other modules call it after the use succeeded.</summary>
public interface IUsageRecorder
{
    Task RecordAsync(UsageEvent usage, string subject, CancellationToken cancellationToken);
}

/// <summary>How often a tool was opened in the period.</summary>
public sealed record ToolUsage(string Code, int Opens);

/// <summary>Activities created and reused on a board in the period.</summary>
public sealed record BoardUsage(string Board, int Created, int Reused);

/// <summary>The workspace's own usage in the last 7, 30 or 90 UTC days.</summary>
public sealed record WorkspaceUsageResponse(int Days, DateOnly From, DateOnly To, IReadOnlyList<ToolUsage> Tools, IReadOnlyList<BoardUsage> Boards);

/// <summary>Platform-wide totals of one UTC day.</summary>
public sealed record UsageDay(DateOnly Day, int ToolOpens, int ActivitiesCreated, int ActivitiesReused);

/// <summary>
/// Platform-wide usage for staff: totals per tool and board, per day, and how many workspaces used the toolbox. Never a
/// workspace's own numbers or identity.
/// </summary>
public sealed record PlatformUsageResponse(
    int Days,
    DateOnly From,
    DateOnly To,
    int Workspaces,
    IReadOnlyList<ToolUsage> Tools,
    IReadOnlyList<BoardUsage> Boards,
    IReadOnlyList<UsageDay> Daily);

internal sealed record UsageTotal(UsageEvent Event, string Subject, int Count);

internal sealed record UsageDailyTotal(DateOnly Day, UsageEvent Event, int Count);
