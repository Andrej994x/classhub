using ClassHub.Core.Modules.Sites.Contracts;
using ClassHub.Core.Modules.Sites.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Sites.Persistence;

internal sealed class SiteConfiguration : IEntityTypeConfiguration<Site>
{
    public void Configure(EntityTypeBuilder<Site> builder)
    {
        builder.ToTable("Sites", t =>
        {
            t.HasCheckConstraint("CK_Sites_DraftJson", "ISJSON([DraftJson]) = 1");
            t.HasCheckConstraint("CK_Sites_PublishedJson", "[PublishedJson] IS NULL OR ISJSON([PublishedJson]) = 1");
            t.HasCheckConstraint("CK_Sites_HiddenSectionsJson", "[HiddenSectionsJson] IS NULL OR ISJSON([HiddenSectionsJson]) = 1");
            t.HasCheckConstraint("CK_Sites_Hidden", "([HiddenAtUtc] IS NULL AND [HiddenReason] IS NULL) OR ([HiddenAtUtc] IS NOT NULL AND [HiddenReason] IS NOT NULL)");
            t.HasCheckConstraint("CK_Sites_Slug", $"LEN([Slug]) BETWEEN {SiteSlugs.MinLength} AND {SiteSlugs.MaxLength} AND [Slug] NOT LIKE '%[^a-z0-9-]%'");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        // One site per workspace; the slug is unique across the platform (it is the public address).
        builder.HasIndex(x => x.WorkspaceId).IsUnique();
        builder.HasIndex(x => x.Slug).IsUnique();
        builder.Property(x => x.Slug).HasMaxLength(SiteSlugs.MaxLength).IsUnicode(false).UseCollation("Latin1_General_100_BIN2").IsRequired();
        builder.Property(x => x.Template).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Palette).HasMaxLength(20).IsUnicode(false).IsRequired();
        builder.Property(x => x.Language).HasMaxLength(Site.LanguageMaxLength).IsRequired();
        builder.Property(x => x.DraftJson).IsRequired();
        builder.Property(x => x.HiddenReason).HasMaxLength(Site.HiddenReasonMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Site.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}
