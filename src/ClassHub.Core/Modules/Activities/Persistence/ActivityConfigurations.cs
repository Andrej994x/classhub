using ClassHub.Core.Modules.Activities.Domain;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Activities.Persistence;

internal sealed class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.ToTable("Activities", t =>
        {
            t.HasCheckConstraint("CK_Activities_Document", "ISJSON([DocumentJson]) = 1 AND LEFT(LTRIM([DocumentJson]), 1) = '{'");
            t.HasCheckConstraint("CK_Activities_Title", "LEN([Title]) >= 1");
            t.HasCheckConstraint("CK_Activities_Size", "[SizeBytes] >= 2");
            t.HasCheckConstraint("CK_Activities_SchemaVersion", "[SchemaVersion] >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Title).HasMaxLength(Activity.TitleMaxLength).IsRequired();
        builder.Property(x => x.ToolType).HasMaxLength(40).IsUnicode(false).IsRequired();
        builder.Property(x => x.DocumentJson).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.ArchivedAtUtc, x.UpdatedAtUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Activity.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Activity.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}
