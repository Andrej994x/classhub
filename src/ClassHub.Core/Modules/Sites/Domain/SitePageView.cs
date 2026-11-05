using ClassHub.Core.SharedKernel.Tenancy;

namespace ClassHub.Core.Modules.Sites.Domain;

/// <summary>
/// Page views of a public site per UTC day (D-32): a server-side counter only. No cookies, no IP addresses, no user
/// agents and no per-visit rows are stored.
/// </summary>
public sealed class SitePageView : ITenantOwned
{
    public Guid WorkspaceId { get; set; }

    public Guid SiteId { get; set; }

    public DateOnly Day { get; set; }

    public int Views { get; set; }
}
