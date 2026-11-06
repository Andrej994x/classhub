using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.DiagnosticTests.Contracts;
using ClassHub.Core.Modules.Leads.Domain;
using ClassHub.Core.Modules.Offers.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Leads.Persistence;

internal sealed class LeadConfiguration : IEntityTypeConfiguration<Lead>
{
    public void Configure(EntityTypeBuilder<Lead> builder)
    {
        builder.ToTable("Leads", t =>
        {
            t.HasCheckConstraint("CK_Leads_Type", "[Type] IN (1, 2, 3)");
            t.HasCheckConstraint("CK_Leads_Attempt", "([Type] = 3 AND [AttemptId] IS NOT NULL) OR ([Type] <> 3 AND [AttemptId] IS NULL)");
            t.HasCheckConstraint("CK_Leads_Status", "[Status] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_Leads_Name", "LEN([Name]) > 0");
            t.HasCheckConstraint("CK_Leads_Email", "[Email] LIKE '%_@_%'");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Name).HasMaxLength(Lead.NameMaxLength).IsRequired();
        builder.Property(x => x.Email).HasMaxLength(Lead.EmailMaxLength).IsRequired();
        builder.Property(x => x.Phone).HasMaxLength(Lead.PhoneMaxLength);
        builder.Property(x => x.Message).HasMaxLength(Lead.MessageMaxLength);
        builder.Property(x => x.OfferTitle).HasMaxLength(Lead.OfferTitleMaxLength);
        builder.Property(x => x.ConsentVersion).HasMaxLength(Lead.ConsentVersionMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.CreatedAtUtc });
        builder.HasIndex(x => new { x.WorkspaceId, x.Status });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Lead.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        // Same-workspace offer only (composite FK); subjects come from the catalog.
        builder.HasOne(OfferForeignKeyTargets.Offer, navigationName: null).WithMany()
            .HasForeignKey(nameof(Lead.WorkspaceId), nameof(Lead.OfferId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Lead.SubjectId)).OnDelete(DeleteBehavior.NoAction);
        // A diagnostic request belongs to an attempt of the same workspace, and an attempt gets at most one request.
        builder.HasOne(DiagnosticTestForeignKeyTargets.Attempt, navigationName: null).WithMany()
            .HasForeignKey(nameof(Lead.WorkspaceId), nameof(Lead.AttemptId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => x.AttemptId).IsUnique().HasFilter("[AttemptId] IS NOT NULL");
        // A converted request points to a student of the same workspace (several requests may become one student).
        builder.HasOne(ClassHub.Core.Modules.Students.Contracts.StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(Lead.WorkspaceId), nameof(Lead.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LeadNoteConfiguration : IEntityTypeConfiguration<LeadNote>
{
    public void Configure(EntityTypeBuilder<LeadNote> builder)
    {
        builder.ToTable("LeadNotes", t => t.HasCheckConstraint("CK_LeadNotes_Text", "LEN([Text]) > 0"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).HasMaxLength(LeadNote.TextMaxLength).IsRequired();
        builder.HasIndex(x => new { x.WorkspaceId, x.LeadId, x.CreatedAtUtc });
        builder.HasTenantReference<LeadNote, Lead>(nameof(LeadNote.LeadId));
    }
}
