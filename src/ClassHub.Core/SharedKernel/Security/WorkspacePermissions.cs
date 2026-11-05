using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.SharedKernel.Security;

/// <summary>
/// Code-defined workspace permissions (no database-editable permission matrix in the MVP). Modules add
/// their permissions here; <see cref="WorkspaceRolePermissions"/> maps roles to permissions.
/// </summary>
public static class WorkspacePermissions
{
    public const string WorkspaceRead = "workspace.read";
    public const string WorkspaceManage = "workspace.manage";
    public const string ProfileRead = "profile.read";
    public const string ProfileManage = "profile.manage";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        WorkspaceRead,
        WorkspaceManage,
        ProfileRead,
        ProfileManage,
    };
}

/// <summary>Role → permission map. Future academy roles get subsets.</summary>
public static class WorkspaceRolePermissions
{
    public static bool Has(WorkspaceRole role, string permission) => role switch
    {
        WorkspaceRole.Owner => WorkspacePermissions.All.Contains(permission),
        _ => false,
    };
}

/// <summary>Authorization policy names for workspace permissions (<c>ws:&lt;permission&gt;</c>).</summary>
public static class WorkspacePolicies
{
    public const string Prefix = "ws:";

    public static string For(string permission) => Prefix + permission;

    public const string Read = Prefix + WorkspacePermissions.WorkspaceRead;
    public const string Manage = Prefix + WorkspacePermissions.WorkspaceManage;
}

/// <summary>Platform roles (ASP.NET Core Identity roles). They grant no workspace access.</summary>
public static class PlatformRoles
{
    public const string Owner = "PlatformOwner";
    public const string Admin = "PlatformAdmin";

    public static readonly Guid OwnerRoleId = new("6f1c2d3e-0a4b-4c5d-8e9f-000000000001");
    public static readonly Guid AdminRoleId = new("6f1c2d3e-0a4b-4c5d-8e9f-000000000002");
}
