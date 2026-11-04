using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Workspaces.Domain;

/// <summary>
/// A teacher's working space: the tenant boundary. Students, lessons, materials, offers and finance
/// records of a teacher live in exactly one workspace.
/// </summary>
public sealed class Workspace
{
    public const int NameMaxLength = 200;
    public const int TimeZoneMaxLength = 64;
    public const int LocaleMaxLength = 10;
    public const string DefaultCurrency = "MKD";

    public Guid Id { get; set; }

    public required string Name { get; set; }

    /// <summary>IANA time zone used for availability and recurrence.</summary>
    public required string TimeZone { get; set; }

    /// <summary>Default ISO 4217 currency of the workspace (D-21).</summary>
    public required string Currency { get; set; }

    /// <summary>Default language for the workspace's own content (mk/en).</summary>
    public required string Locale { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public byte[] RowVersion { get; set; } = [];
}

/// <summary>A user's membership in a workspace.</summary>
public sealed class WorkspaceMember : ITenantOwned
{
    public Guid Id { get; set; }

    public Guid WorkspaceId { get; set; }

    public Guid UserId { get; set; }

    public WorkspaceRole Role { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    /// <summary>Removed memberships are kept for history (future academy roles).</summary>
    public DateTime? RevokedAtUtc { get; set; }
}
