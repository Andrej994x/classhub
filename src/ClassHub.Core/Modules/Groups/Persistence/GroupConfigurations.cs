using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Groups.Persistence;

internal sealed class GroupConfiguration : IEntityTypeConfiguration<Group>
{
    public void Configure(EntityTypeBuilder<Group> builder)
    {
        builder.ToTable("Groups", t => t.HasCheckConstraint("CK_Groups_Name", "LEN([Name]) >= 1"));
        builder.HasKey(x => x.Id);
        // Principal key for composite tenant foreign keys (members; group lessons in R2-P4).
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Name).HasMaxLength(Group.NameMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(Group.DescriptionMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.ArchivedAtUtc, x.Name });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Group.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Group.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class GroupMemberConfiguration : IEntityTypeConfiguration<GroupMember>
{
    public void Configure(EntityTypeBuilder<GroupMember> builder)
    {
        builder.ToTable("GroupMembers", t => t.HasCheckConstraint("CK_GroupMembers_Removed", "[RemovedAtUtc] IS NULL OR [RemovedAtUtc] >= [AddedAtUtc]"));
        builder.HasKey(x => new { x.GroupId, x.StudentId });
        builder.HasTenantReference<GroupMember, Group>(nameof(GroupMember.GroupId));
        // A member is a student of the same workspace: the database rejects any other (composite tenant FK).
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(GroupMember.WorkspaceId), nameof(GroupMember.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.RemovedAtUtc });
    }
}
