using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Students.Domain;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Students.Persistence;

internal sealed class StudentConfiguration : IEntityTypeConfiguration<Student>
{
    public void Configure(EntityTypeBuilder<Student> builder)
    {
        builder.ToTable("Students", t =>
        {
            t.HasCheckConstraint("CK_Students_FirstName", "LEN([FirstName]) >= 1");
            t.HasCheckConstraint("CK_Students_Email", "[Email] IS NULL OR ([Email] = LOWER([Email]) AND [Email] LIKE '%_@_%')");
        });
        builder.HasKey(x => x.Id);
        // Principal key for composite tenant foreign keys (subjects, notes, converted leads; lessons later).
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.FirstName).HasMaxLength(Student.NameMaxLength).IsRequired();
        builder.Property(x => x.LastName).HasMaxLength(Student.NameMaxLength);
        builder.Property(x => x.Email).HasMaxLength(Student.EmailMaxLength);
        builder.Property(x => x.Phone).HasMaxLength(Student.PhoneMaxLength);
        builder.Property(x => x.GuardianName).HasMaxLength(Student.GuardianNameMaxLength);
        builder.Property(x => x.GuardianEmail).HasMaxLength(Student.EmailMaxLength);
        builder.Property(x => x.GuardianPhone).HasMaxLength(Student.PhoneMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // No duplicates by email within a workspace (archived students included): the dedupe key of conversions.
        builder.HasIndex(x => new { x.WorkspaceId, x.Email }).IsUnique().HasFilter("[Email] IS NOT NULL");
        builder.HasIndex(x => new { x.WorkspaceId, x.ArchivedAtUtc, x.LastName });
        // One student record per account and workspace; the account may be a student of other teachers too (D-16).
        builder.HasIndex(x => new { x.WorkspaceId, x.UserId }).IsUnique().HasFilter("[UserId] IS NOT NULL");
        builder.HasIndex(x => x.UserId).HasFilter("[UserId] IS NOT NULL");
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Student.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        // The request a student came from belongs to the same workspace (composite tenant FK).
        builder.HasOne(ClassHub.Core.Modules.Leads.Contracts.LeadForeignKeyTargets.Lead, navigationName: null).WithMany()
            .HasForeignKey(nameof(Student.WorkspaceId), nameof(Student.SourceLeadId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StudentSubjectConfiguration : IEntityTypeConfiguration<StudentSubject>
{
    public void Configure(EntityTypeBuilder<StudentSubject> builder)
    {
        builder.ToTable("StudentSubjects");
        builder.HasKey(x => new { x.StudentId, x.SubjectId });
        builder.HasTenantReference<StudentSubject, Student>(nameof(StudentSubject.StudentId));
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(StudentSubject.SubjectId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class StudentNoteConfiguration : IEntityTypeConfiguration<StudentNote>
{
    public void Configure(EntityTypeBuilder<StudentNote> builder)
    {
        builder.ToTable("StudentNotes", t => t.HasCheckConstraint("CK_StudentNotes_Text", "LEN([Text]) >= 1"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Text).HasMaxLength(StudentNote.TextMaxLength).IsRequired();
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.CreatedAtUtc });
        builder.HasTenantReference<StudentNote, Student>(nameof(StudentNote.StudentId));
    }
}

internal sealed class StudentInvitationConfiguration : IEntityTypeConfiguration<StudentInvitation>
{
    public void Configure(EntityTypeBuilder<StudentInvitation> builder)
    {
        builder.ToTable("StudentInvitations", t => t.HasCheckConstraint("CK_StudentInvitations_Email", "[Email] = LOWER([Email]) AND [Email] LIKE '%_@_%'"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Email).HasMaxLength(Student.EmailMaxLength).IsRequired();
        builder.Property(x => x.TokenHash).HasMaxLength(32).IsFixedLength().IsRequired();
        builder.HasIndex(x => x.TokenHash).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId, x.CreatedAtUtc });
        builder.HasTenantReference<StudentInvitation, Student>(nameof(StudentInvitation.StudentId));
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.AcceptedByUserId).OnDelete(DeleteBehavior.NoAction);
    }
}
