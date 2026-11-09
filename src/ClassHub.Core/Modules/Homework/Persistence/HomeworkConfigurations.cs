using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Homework.Domain;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Homework.Persistence;

internal sealed class HomeworkTaskConfiguration : IEntityTypeConfiguration<HomeworkTask>
{
    public void Configure(EntityTypeBuilder<HomeworkTask> builder)
    {
        builder.ToTable("Homework", t =>
        {
            t.HasCheckConstraint("CK_Homework_Title", "LEN([Title]) >= 1");
            t.HasCheckConstraint("CK_Homework_Due", "([DueDate] IS NULL AND [DueAtUtc] IS NULL) OR ([DueDate] IS NOT NULL AND [DueAtUtc] IS NOT NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(HomeworkTask.TitleMaxLength).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(HomeworkTask.DescriptionMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.ArchivedAtUtc, x.UpdatedAtUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(HomeworkTask.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        // The lesson belongs to the same workspace (composite tenant FK).
        builder.HasOne(LessonForeignKeyTargets.Lesson, navigationName: null).WithMany()
            .HasForeignKey(nameof(HomeworkTask.WorkspaceId), nameof(HomeworkTask.LessonId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class HomeworkAssignmentConfiguration : IEntityTypeConfiguration<HomeworkAssignment>
{
    public void Configure(EntityTypeBuilder<HomeworkAssignment> builder)
    {
        builder.ToTable("HomeworkAssignments", t =>
        {
            t.HasCheckConstraint("CK_HomeworkAssignments_Status", "[Status] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_HomeworkAssignments_Submitted", "[Status] = 1 OR [SubmittedAtUtc] IS NOT NULL");
            t.HasCheckConstraint("CK_HomeworkAssignments_Reviewed", "([Status] = 3 AND [ReviewedAtUtc] IS NOT NULL) OR ([Status] <> 3 AND [ReviewedAtUtc] IS NULL)");
            t.HasCheckConstraint("CK_HomeworkAssignments_File",
                "([FileId] IS NULL AND [FileName] IS NULL AND [ContentType] IS NULL AND [SizeBytes] IS NULL) OR ([FileId] IS NOT NULL AND [FileName] IS NOT NULL AND [ContentType] IS NOT NULL AND [SizeBytes] > 0)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.AnswerText).HasMaxLength(HomeworkAssignment.AnswerMaxLength);
        builder.Property(x => x.TeacherComment).HasMaxLength(HomeworkAssignment.CommentMaxLength);
        builder.Property(x => x.FileName).HasMaxLength(HomeworkFiles.FileNameMaxLength);
        builder.Property(x => x.ContentType).HasMaxLength(StoredFile.ContentTypeMaxLength).IsUnicode(false);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.HomeworkId, x.StudentId }).IsUnique();
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId });
        builder.HasIndex(x => x.FileId).IsUnique().HasFilter("[FileId] IS NOT NULL");
        builder.HasTenantReference<HomeworkAssignment, HomeworkTask>(nameof(HomeworkAssignment.HomeworkId));
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(HomeworkAssignment.WorkspaceId), nameof(HomeworkAssignment.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<StoredFile>().WithMany()
            .HasForeignKey(nameof(HomeworkAssignment.WorkspaceId), nameof(HomeworkAssignment.FileId))
            .HasPrincipalKey(nameof(StoredFile.WorkspaceId), nameof(StoredFile.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}
