using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace ClassHub.Api.Platform;

/// <summary>
/// Reads the Vite build manifest (<c>wwwroot/dist/.vite/manifest.json</c>) and resolves entries to their hashed files.
/// The manifest is re-read when the file changes (so <c>vite build --watch</c> works in development). Without a build
/// the pages still render, only without styles and islands (a warning is logged once).
/// </summary>
public sealed partial class ViteManifest(IWebHostEnvironment environment, ILogger<ViteManifest> logger)
{
    public const string ManifestPath = "dist/.vite/manifest.json";
    public const string AssetBase = "/dist/";

    private readonly Lock _gate = new();
    private Dictionary<string, Chunk> _chunks = [];
    private DateTimeOffset? _loadedVersion;
    private bool _warned;

    internal sealed record Chunk(
        [property: JsonPropertyName("file")] string File,
        [property: JsonPropertyName("css")] IReadOnlyList<string>? Css,
        [property: JsonPropertyName("imports")] IReadOnlyList<string>? Imports);

    /// <summary>The script file and every stylesheet an entry needs (its own and those of its imports), or null.</summary>
    public (string? Script, IReadOnlyList<string> Styles)? Resolve(string entry)
    {
        var chunks = Load();
        if (!chunks.TryGetValue(entry, out var chunk))
        {
            return null;
        }

        var styles = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Collect(Chunk current)
        {
            foreach (var css in current.Css ?? [])
            {
                if (!styles.Contains(css))
                {
                    styles.Add(css);
                }
            }

            foreach (var import in current.Imports ?? [])
            {
                if (seen.Add(import) && chunks.TryGetValue(import, out var imported))
                {
                    Collect(imported);
                }
            }
        }

        Collect(chunk);
        var isStylesheet = chunk.File.EndsWith(".css", StringComparison.OrdinalIgnoreCase);
        if (isStylesheet)
        {
            styles.Insert(0, chunk.File);
        }

        return (isStylesheet ? null : AssetBase + chunk.File, [.. styles.Select(s => AssetBase + s)]);
    }

    private Dictionary<string, Chunk> Load()
    {
        var file = environment.WebRootFileProvider.GetFileInfo(ManifestPath);
        lock (_gate)
        {
            if (!file.Exists)
            {
                if (!_warned)
                {
                    _warned = true;
                    LogMissing(logger, ManifestPath);
                }

                return [];
            }

            if (_loadedVersion != file.LastModified)
            {
                using var stream = file.CreateReadStream();
                _chunks = JsonSerializer.Deserialize<Dictionary<string, Chunk>>(stream) ?? [];
                _loadedVersion = file.LastModified;
            }

            return _chunks;
        }
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "The Vite manifest {Path} was not found: public pages render without styles and islands. Run `npm run build` in web/.")]
    private static partial void LogMissing(ILogger logger, string path);
}

/// <summary>Remembers which assets a response already references, so islands used twice load their files once.</summary>
internal static class ViteRequestAssets
{
    private static readonly object Key = new();

    public static bool FirstUse(HttpContext http, string asset)
    {
        if (http.Items[Key] is not HashSet<string> used)
        {
            used = new HashSet<string>(StringComparer.Ordinal);
            http.Items[Key] = used;
        }

        return used.Add(asset);
    }
}

/// <summary><c>&lt;vite-asset entry="src/public/public.css" /&gt;</c>: the hashed stylesheet/script tags of a Vite entry.</summary>
[HtmlTargetElement("vite-asset", TagStructure = TagStructure.WithoutEndTag)]
public sealed class ViteAssetTagHelper : TagHelper
{
    private readonly ViteManifest _manifest;

    public ViteAssetTagHelper(ViteManifest manifest) => _manifest = manifest;

    public string Entry { get; set; } = string.Empty;

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        output.TagName = null;
        output.Content.SetHtmlContent(ViteTags.For(_manifest, ViewContext.HttpContext, Entry));
    }
}

/// <summary>
/// <c>&lt;vite-island name="copy-link" props="@obj" /&gt;</c>: a mount point for the React island
/// <c>web/src/islands/{name}.tsx</c> with its props as JSON in <c>data-props</c> (attribute-encoded), plus the island's
/// script and styles. The island renders into the element; the server HTML works without it.
/// </summary>
[HtmlTargetElement("vite-island", TagStructure = TagStructure.WithoutEndTag)]
public sealed partial class ViteIslandTagHelper : TagHelper
{
    // Readable non-ASCII text; HTML-sensitive characters stay escaped (and the attribute is HTML-encoded as well).
    private static readonly JsonSerializerOptions PropsJson = new(JsonSerializerDefaults.Web)
    {
        Encoder = JavaScriptEncoder.Create(System.Text.Unicode.UnicodeRanges.All),
    };
    private readonly ViteManifest _manifest;

    public ViteIslandTagHelper(ViteManifest manifest) => _manifest = manifest;

    public string Name { get; set; } = string.Empty;

    public object? Props { get; set; }

    [ViewContext, HtmlAttributeNotBound]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        if (!IslandName().IsMatch(Name))
        {
            throw new InvalidOperationException($"Invalid island name '{Name}'.");
        }

        output.TagName = "div";
        output.TagMode = TagMode.StartTagAndEndTag;
        output.Attributes.SetAttribute("data-island", Name);
        output.Attributes.SetAttribute("data-props", JsonSerializer.Serialize(Props ?? new { }, PropsJson));
        output.PostElement.SetHtmlContent(ViteTags.For(_manifest, ViewContext.HttpContext, $"src/islands/{Name}.tsx"));
    }

    [System.Text.RegularExpressions.GeneratedRegex("^[a-z][a-z0-9-]*$")]
    private static partial System.Text.RegularExpressions.Regex IslandName();
}

internal static class ViteTags
{
    public static string For(ViteManifest manifest, HttpContext http, string entry)
    {
        if (manifest.Resolve(entry) is not (var script, var styles))
        {
            return string.Empty;
        }

        var html = new System.Text.StringBuilder();
        foreach (var style in styles.Where(s => ViteRequestAssets.FirstUse(http, s)))
        {
            html.Append("<link rel=\"stylesheet\" href=\"").Append(HtmlEncoder.Default.Encode(style)).Append("\" />");
        }

        if (script is not null && ViteRequestAssets.FirstUse(http, script))
        {
            html.Append("<script type=\"module\" src=\"").Append(HtmlEncoder.Default.Encode(script)).Append("\"></script>");
        }

        return html.ToString();
    }
}
