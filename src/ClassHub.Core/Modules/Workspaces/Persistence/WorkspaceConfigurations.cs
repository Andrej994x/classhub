using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Workspaces.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Workspaces.Persistence;

internal sealed class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> builder)
    {
        builder.ToTable("Workspaces", t => t.HasCheckConstraint("CK_Workspaces_Currency", "[Currency] LIKE '[A-Z][A-Z][A-Z]'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Name).HasMaxLength(Workspace.NameMaxLength).IsRequired();
        builder.Property(x => x.TimeZone).HasMaxLength(Workspace.TimeZoneMaxLength).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(x => x.Locale).HasMaxLength(Workspace.LocaleMaxLength).IsRequired();
        builder.Property(x => x.RowVersion).IsRowVersion();
    }
}

internal sealed class WorkspaceMemberConfiguration : IEntityTypeConfiguration<WorkspaceMember>
{
    public void Configure(EntityTypeBuilder<WorkspaceMember> builder)
    {
        builder.ToTable("WorkspaceMembers");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Role).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        // One active membership per user and workspace.
        builder.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique().HasFilter("[RevokedAtUtc] IS NULL");
        builder.HasIndex(x => x.UserId);
    }
}
