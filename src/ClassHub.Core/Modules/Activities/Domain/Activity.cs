using System.Text.Json;
using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Activities.Domain;

/// <summary>
/// A teaching activity the teacher saved from a board (M09 Activity Library, R3-P2): a title, an optional subject and
/// the board's document (JSON, with the version of the board's format). Owned by the workspace; deleting is soft
/// (<see cref="ArchivedAtUtc"/>), and a deleted activity no longer counts toward the plan.
/// </summary>
public sealed class Activity : ITenantOwned, IArchivable
{
    public const int TitleMaxLength = 150;

    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public required string Title { get; set; }

    public Guid? SubjectId { get; set; }

    /// <summary>Which board the document belongs to (<see cref="ActivityRules.ToolTypes"/>).</summary>
    public required string ToolType { get; set; }

    /// <summary>The version of the board's document format the document was written in.</summary>
    public int SchemaVersion { get; set; }

    public required string DocumentJson { get; set; }

    /// <summary>The document's size in UTF-8 bytes (what the limit checks).</summary>
    public int SizeBytes { get; set; }

    public Guid? CreatedByUserId { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    /// <summary>When it was deleted (soft delete).</summary>
    public DateTime? ArchivedAtUtc { get; set; }

    public int Version { get; set; }
}

public static class ActivityRules
{
    /// <summary>The boards an activity can belong to (the toolbox's built-in boards) and the format versions they read.</summary>
    public static readonly IReadOnlyDictionary<string, IReadOnlyList<int>> SupportedSchemaVersions = new Dictionary<string, IReadOnlyList<int>>(StringComparer.Ordinal)
    {
        ["drawing-board"] = [1],
        ["geometry-board"] = [1],
    };

    public static IEnumerable<string> ToolTypes => SupportedSchemaVersions.Keys;

    public static bool IsSupported(string? toolType, int? schemaVersion) =>
        toolType is not null && schemaVersion is { } version && SupportedSchemaVersions.TryGetValue(toolType, out var versions) && versions.Contains(version);

    /// <summary>The document as stored: a JSON object (never an array or a value) within the size limit.</summary>
    public static (string Json, int SizeBytes)? Document(JsonElement? document, int maxBytes)
    {
        if (document is not { ValueKind: JsonValueKind.Object } value)
        {
            return null;
        }

        var json = value.GetRawText();
        var size = System.Text.Encoding.UTF8.GetByteCount(json);
        return size <= maxBytes ? (json, size) : null;
    }

    /// <summary>The element types a drawing may contain (format v1): never images, embedded web pages or iframes (D-42).</summary>
    public static readonly IReadOnlySet<string> DrawingElementTypes = new HashSet<string>(StringComparer.Ordinal)
    {
        "rectangle", "diamond", "ellipse", "arrow", "line", "freedraw", "text", "frame",
    };

    public const int MaxDrawingElements = 10_000;

    /// <summary>
    /// What is wrong with the document's content for its board (null: nothing). The drawing board (v1): an
    /// <c>elements</c> array of at most <see cref="MaxDrawingElements"/> objects of the allowed types, no files, an
    /// optional <c>appState</c> object. The geometry board's checks come with that board (R3-P4).
    /// </summary>
    public static string? ContentProblem(string toolType, int schemaVersion, JsonElement document)
    {
        if (toolType != "drawing-board" || schemaVersion != 1)
        {
            return null;
        }

        if (!document.TryGetProperty("elements", out var elements) || elements.ValueKind != JsonValueKind.Array)
        {
            return "A drawing needs its list of elements.";
        }

        if (elements.GetArrayLength() > MaxDrawingElements)
        {
            return $"A drawing has at most {MaxDrawingElements} elements.";
        }

        foreach (var element in elements.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !DrawingElementTypes.Contains(type.GetString()!))
            {
                return "Drawings contain shapes, lines, arrows, text and frames only (no images or embedded pages).";
            }
        }

        if (document.TryGetProperty("files", out var files) && !(files.ValueKind == JsonValueKind.Object && !files.EnumerateObject().Any()))
        {
            return "Drawings carry no files.";
        }

        if (document.TryGetProperty("appState", out var appState) && appState.ValueKind != JsonValueKind.Object)
        {
            return "The drawing's settings must be an object.";
        }

        return null;
    }

    /// <summary>"Title (copy)", cut so it still fits.</summary>
    public static string CopyTitle(string title, string suffix)
    {
        ArgumentNullException.ThrowIfNull(title);
        var room = Activity.TitleMaxLength - suffix.Length;
        return (title.Length > room ? title[..room].TrimEnd() : title) + suffix;
    }
}
