namespace ClassHub.Core.Modules.Toolbox.Domain;

/// <summary>Where a tool runs: an external site opened by its official link, or a board built into ClassHub.</summary>
public enum ToolKind
{
    External = 1,
    BuiltIn = 2,
}

/// <summary>The catalog's tool categories (fixed; names shown in mk/en by the SPA).</summary>
public enum ToolCategory
{
    Geometry = 1,
    Graphing = 2,
    Simulations = 3,
    Chemistry = 4,
    Drawing = 5,
    Other = 6,
}

/// <summary>
/// A teaching tool in the platform-wide catalog (M09, R3-P1), managed by platform staff. Not tenant-owned: every teacher
/// sees the active ones. Tools are deactivated, never deleted; the code is permanent.
/// </summary>
public sealed class Tool
{
    public const int CodeMaxLength = 40;
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 600;
    public const int UrlMaxLength = 400;
    public const int LicenseNoteMaxLength = 300;

    public Guid Id { get; set; }

    public required string Code { get; set; }

    public required string NameMk { get; set; }

    public required string NameEn { get; set; }

    public required string DescriptionMk { get; set; }

    public required string DescriptionEn { get; set; }

    public ToolKind Kind { get; set; }

    public ToolCategory Category { get; set; }

    /// <summary>External tools: the official HTTPS link (its host on the allow-list).</summary>
    public string? Url { get; set; }

    /// <summary>Built-in tools: which board (<see cref="ToolRules.BuiltInKeys"/>).</summary>
    public string? BuiltInKey { get; set; }

    /// <summary>Why the tool may be offered (its license or terms of use), in both UI languages.</summary>
    public string? LicenseNoteMk { get; set; }

    public string? LicenseNoteEn { get; set; }

    public bool IsActive { get; set; } = true;

    public bool IsFeatured { get; set; }

    public int SortOrder { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public int Version { get; set; }
}

/// <summary>A subject the tool is meant for (none: any subject).</summary>
public sealed class ToolSubject
{
    public Guid ToolId { get; set; }

    public Guid SubjectId { get; set; }
}

public static class ToolRules
{
    /// <summary>The boards built into ClassHub (R3-P3, R3-P4).</summary>
    public static readonly IReadOnlyList<string> BuiltInKeys = ["drawing-board", "geometry-board"];

    /// <summary>
    /// The boards that have shipped: only these may be active (a board's tool opens its page). Each board's phase adds
    /// its key here.
    /// </summary>
    public static readonly IReadOnlyList<string> AvailableBuiltInKeys = ["drawing-board"];

    /// <summary>Whether a tool may be shown to teachers: external, or a board that has shipped.</summary>
    public static bool CanBeActive(ToolKind kind, string? builtInKey) =>
        kind == ToolKind.External || (builtInKey is not null && AvailableBuiltInKeys.Contains(builtInKey));

    /// <summary>
    /// An official link: absolute HTTPS on the default port, no credentials, and its host is an allowed domain or a
    /// subdomain of one.
    /// </summary>
    public static bool IsAllowedUrl(string? url, IReadOnlyCollection<string> allowedHosts)
    {
        ArgumentNullException.ThrowIfNull(allowedHosts);
        if (url is null || url.Length > Tool.UrlMaxLength || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort || uri.UserInfo.Length > 0 || uri.HostNameType != UriHostNameType.Dns)
        {
            return false;
        }

        var host = uri.IdnHost.ToLowerInvariant();
        return allowedHosts.Any(allowed => host == allowed || host.EndsWith("." + allowed, StringComparison.Ordinal));
    }
}
