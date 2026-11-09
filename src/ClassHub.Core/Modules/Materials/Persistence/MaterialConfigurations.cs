using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Materials.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using ClassHub.Core.Infrastructure.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Materials.Persistence;

internal sealed class MaterialConfiguration : IEntityTypeConfiguration<Material>
{
    public void Configure(EntityTypeBuilder<Material> builder)
    {
        builder.ToTable("Materials", t =>
        {
            t.HasCheckConstraint("CK_Materials_Title", "LEN([Title]) >= 1");
            t.HasCheckConstraint("CK_Materials_Size", "[SizeBytes] > 0");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(Material.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Material.DescriptionMaxLength);
        builder.Property(x => x.FileName).HasMaxLength(Material.FileNameMaxLength).IsRequired();
        builder.Property(x => x.ContentType).HasMaxLength(StoredFile.ContentTypeMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.ArchivedAtUtc, x.UpdatedAtUtc });
        builder.HasIndex(x => x.FileId).IsUnique();
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Material.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Material.SubjectId)).OnDelete(DeleteBehavior.NoAction);
        // The file belongs to the same workspace (composite tenant FK).
        builder.HasOne<StoredFile>().WithMany()
            .HasForeignKey(nameof(Material.WorkspaceId), nameof(Material.FileId))
            .HasPrincipalKey(nameof(StoredFile.WorkspaceId), nameof(StoredFile.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class MaterialAssignmentConfiguration : IEntityTypeConfiguration<MaterialAssignment>
{
    public void Configure(EntityTypeBuilder<MaterialAssignment> builder)
    {
        builder.ToTable("MaterialAssignments", t =>
            t.HasCheckConstraint("CK_MaterialAssignments_Target", "([StudentId] IS NULL AND [GroupId] IS NOT NULL) OR ([StudentId] IS NOT NULL AND [GroupId] IS NULL)"));
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.MaterialId, x.StudentId }).IsUnique().HasFilter("[StudentId] IS NOT NULL");
        builder.HasIndex(x => new { x.MaterialId, x.GroupId }).IsUnique().HasFilter("[GroupId] IS NOT NULL");
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId });
        builder.HasIndex(x => new { x.WorkspaceId, x.GroupId });
        builder.HasTenantReference<MaterialAssignment, Material>(nameof(MaterialAssignment.MaterialId));
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(MaterialAssignment.WorkspaceId), nameof(MaterialAssignment.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(GroupForeignKeyTargets.Group, navigationName: null).WithMany()
            .HasForeignKey(nameof(MaterialAssignment.WorkspaceId), nameof(MaterialAssignment.GroupId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
