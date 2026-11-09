namespace ClassHub.Core.Modules.Toolbox.Contracts;

/// <summary>
/// A tool as teachers see it: names and descriptions in both UI languages, the kind and category by name, and how to
/// open it (the official link of an external tool, or the key of a built-in board).
/// </summary>
public sealed record ToolResponse(
    Guid Id,
    string Code,
    string NameMk,
    string NameEn,
    string DescriptionMk,
    string DescriptionEn,
    string Kind,
    string Category,
    string? Url,
    string? BuiltInKey,
    IReadOnlyList<Guid> SubjectIds,
    bool IsFeatured,
    string? LicenseNote);

/// <summary>The teacher catalog: the active tools that match, featured first; and every category for the filter.</summary>
public sealed record ToolboxResponse(IReadOnlyList<ToolResponse> Tools, IReadOnlyList<string> Categories);

/// <summary>A tool for platform staff: the teacher view plus the state they manage.</summary>
public sealed record AdminToolResponse(ToolResponse Tool, bool IsActive, int SortOrder, DateTime UpdatedAtUtc, int Version);

/// <summary>
/// Create (no version) or change (the version that was loaded) a tool. The code is set once. External tools need an
/// HTTPS link on an allowed host; built-in tools name their board.
/// </summary>
public sealed record SaveToolRequest(
    string? Code,
    string? NameMk,
    string? NameEn,
    string? DescriptionMk,
    string? DescriptionEn,
    string? Kind,
    string? Category,
    string? Url,
    string? BuiltInKey,
    IReadOnlyList<Guid>? SubjectIds,
    string? LicenseNote,
    bool? IsActive,
    bool? IsFeatured,
    int? SortOrder,
    int? Version);
