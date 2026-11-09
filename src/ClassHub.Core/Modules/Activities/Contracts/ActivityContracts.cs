using System.Text.Json;

namespace ClassHub.Core.Modules.Activities.Contracts;

/// <summary>An activity in the library list (without its document).</summary>
public sealed record ActivityListItem(Guid Id, string Title, Guid? SubjectId, string ToolType, int SchemaVersion, int SizeBytes, DateTime UpdatedAtUtc, int Version);

public sealed record ActivityListResponse(IReadOnlyList<ActivityListItem> Items, int Total, int Page, int PageSize);

/// <summary>An activity with its document, to open it on its board.</summary>
public sealed record ActivityResponse(
    Guid Id,
    string Title,
    Guid? SubjectId,
    string ToolType,
    int SchemaVersion,
    JsonElement Document,
    int SizeBytes,
    DateTime CreatedAtUtc,
    DateTime UpdatedAtUtc,
    int Version);

/// <summary>
/// Save a new activity (no version) or change one (the version that was loaded). The document is a JSON object in the
/// board's format; the tool type never changes after the first save.
/// </summary>
public sealed record SaveActivityRequest(string? Title, Guid? SubjectId, string? ToolType, int? SchemaVersion, JsonElement? Document, int? Version);

public sealed record ActivityVersionRequest(int? Version);
