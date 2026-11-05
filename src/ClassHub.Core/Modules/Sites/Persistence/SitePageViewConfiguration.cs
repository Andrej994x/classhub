using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Sites.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Sites.Persistence;

internal sealed class SitePageViewConfiguration : IEntityTypeConfiguration<SitePageView>
{
    public void Configure(EntityTypeBuilder<SitePageView> builder)
    {
        builder.ToTable("SitePageViews", t => t.HasCheckConstraint("CK_SitePageViews_Views", "[Views] >= 0"));
        builder.HasKey(x => new { x.SiteId, x.Day });
        builder.HasIndex(x => new { x.WorkspaceId, x.Day });
        builder.HasTenantReference<SitePageView, Site>(nameof(SitePageView.SiteId));
    }
}
