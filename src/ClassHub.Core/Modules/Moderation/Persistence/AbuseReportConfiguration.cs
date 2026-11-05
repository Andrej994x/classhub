using ClassHub.Core.Modules.Moderation.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Moderation.Persistence;

internal sealed class AbuseReportConfiguration : IEntityTypeConfiguration<AbuseReport>
{
    public void Configure(EntityTypeBuilder<AbuseReport> builder)
    {
        builder.ToTable("AbuseReports", t =>
        {
            t.HasCheckConstraint("CK_AbuseReports_Target",
                "([Target] = 1 AND [SectionType] IS NULL AND [TestId] IS NULL) OR ([Target] = 2 AND [SectionType] IS NOT NULL AND [TestId] IS NULL) OR ([Target] = 3 AND [SectionType] IS NULL AND [TestId] IS NOT NULL)");
            t.HasCheckConstraint("CK_AbuseReports_Reason", "[Reason] BETWEEN 1 AND 6");
            t.HasCheckConstraint("CK_AbuseReports_Status", "[Status] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_AbuseReports_Resolution", "([Status] = 1 AND [ResolvedAtUtc] IS NULL) OR ([Status] <> 1 AND [ResolvedAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SectionType).HasMaxLength(AbuseReport.SectionTypeMaxLength).IsUnicode(false);
        builder.Property(x => x.Details).HasMaxLength(AbuseReport.DetailsMaxLength);
        builder.Property(x => x.ReporterEmail).HasMaxLength(AbuseReport.EmailMaxLength);
        builder.Property(x => x.ResolutionNote).HasMaxLength(AbuseReport.NoteMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.Status, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(AbuseReport.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}
