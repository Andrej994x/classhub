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

    public const int MaxGeometryObjects = 2000;
    public const double GeometryCoordinateLimit = 1_000_000;

    /// <summary>
    /// What is wrong with the document's content for its board (null: nothing). The drawing board (v1): an
    /// <c>elements</c> array of at most <see cref="MaxDrawingElements"/> objects of the allowed types, no files, an
    /// optional <c>appState</c> object. The geometry board (v1, D-33): see <see cref="GeometryProblem"/>.
    /// </summary>
    public static string? ContentProblem(string toolType, int schemaVersion, JsonElement document) =>
        (toolType, schemaVersion) switch
        {
            ("drawing-board", 1) => DrawingProblem(document),
            ("geometry-board", 1) => GeometryProblem(document),
            _ => null,
        };

    private static string? DrawingProblem(JsonElement document)
    {
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

    /// <summary>
    /// The geometry board's construction (v1, ClassHub's own format, D-33): <c>format</c> "classhub.geometry"; an
    /// <c>objects</c> array of at most <see cref="MaxGeometryObjects"/>, each with a unique name and a known type: free
    /// points with finite coordinates, segments and lines on two points, circles on a center and a point, triangles and
    /// angles on three points, always distinct points named before; an optional <c>view</c> (visible area, axes, grid).
    /// </summary>
    private static string? GeometryProblem(JsonElement document)
    {
        if (!document.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String || format.GetString() != "classhub.geometry")
        {
            return "A construction names its format (classhub.geometry).";
        }

        if (!document.TryGetProperty("objects", out var objects) || objects.ValueKind != JsonValueKind.Array)
        {
            return "A construction needs its list of objects.";
        }

        if (objects.GetArrayLength() > MaxGeometryObjects)
        {
            return $"A construction has at most {MaxGeometryObjects} objects.";
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        var points = new HashSet<string>(StringComparer.Ordinal);
        foreach (var o in objects.EnumerateArray())
        {
            if (o.ValueKind != JsonValueKind.Object || !o.TryGetProperty("id", out var idValue) || idValue.ValueKind != JsonValueKind.String
                || !IsGeometryName(idValue.GetString()!) || !names.Add(idValue.GetString()!))
            {
                return "Every object has its own name (letters, digits, _ and ', up to 40).";
            }

            var type = o.TryGetProperty("type", out var typeValue) && typeValue.ValueKind == JsonValueKind.String ? typeValue.GetString() : null;
            var valid = type switch
            {
                "point" => IsCoordinate(o, "x") && IsCoordinate(o, "y"),
                "segment" or "line" => References(o, "points", 2, points),
                "circle" => IsReference(o, "center", points) && IsReference(o, "through", points)
                    && o.GetProperty("center").GetString() != o.GetProperty("through").GetString(),
                "triangle" or "angle" => References(o, "points", 3, points),
                _ => false,
            };
            if (!valid)
            {
                return "Objects are points, segments, lines, circles, triangles or angles built on distinct points named before them.";
            }

            if (type == "point")
            {
                points.Add(idValue.GetString()!);
            }
        }

        if (document.TryGetProperty("view", out var view))
        {
            var box = view.ValueKind == JsonValueKind.Object && view.TryGetProperty("boundingBox", out var b) ? b : default;
            if (view.ValueKind != JsonValueKind.Object
                || (box.ValueKind != JsonValueKind.Undefined && (box.ValueKind != JsonValueKind.Array || box.GetArrayLength() != 4 || !box.EnumerateArray().All(IsFinite)))
                || (view.TryGetProperty("axis", out var axis) && axis.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                || (view.TryGetProperty("grid", out var grid) && grid.ValueKind is not (JsonValueKind.True or JsonValueKind.False)))
            {
                return "The view is a visible area of four numbers and the axes and grid switches.";
            }
        }

        return null;
    }

    private static bool IsGeometryName(string name) =>
        name.Length is >= 1 and <= 40 && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '\'');

    private static bool IsFinite(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var d) && double.IsFinite(d) && Math.Abs(d) <= GeometryCoordinateLimit;

    private static bool IsCoordinate(JsonElement o, string name) => o.TryGetProperty(name, out var value) && IsFinite(value);

    private static bool IsReference(JsonElement o, string name, HashSet<string> points) =>
        o.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && points.Contains(value.GetString()!);

    private static bool References(JsonElement o, string name, int count, HashSet<string> points)
    {
        if (!o.TryGetProperty(name, out var list) || list.ValueKind != JsonValueKind.Array || list.GetArrayLength() != count)
        {
            return false;
        }

        var ids = list.EnumerateArray().Select(v => v.ValueKind == JsonValueKind.String ? v.GetString() : null).ToList();
        return ids.All(id => id is not null && points.Contains(id)) && ids.Distinct(StringComparer.Ordinal).Count() == count;
    }

    /// <summary>"Title (copy)", cut so it still fits.</summary>
    public static string CopyTitle(string title, string suffix)
    {
        ArgumentNullException.ThrowIfNull(title);
        var room = Activity.TitleMaxLength - suffix.Length;
        return (title.Length > room ? title[..room].TrimEnd() : title) + suffix;
    }
}
