using ClassHub.Core.Infrastructure.Tenancy;
using ClassHub.Core.Modules.Catalog.Contracts;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Lessons.Persistence;

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("Lessons", t =>
        {
            t.HasCheckConstraint("CK_Lessons_Time", "[EndUtc] > [StartUtc]");
            t.HasCheckConstraint("CK_Lessons_Kind", "([Kind] = 1 AND [GroupId] IS NULL) OR ([Kind] = 2 AND [GroupId] IS NOT NULL)");
            t.HasCheckConstraint("CK_Lessons_Mode", "[Mode] IN (1, 2)");
            t.HasCheckConstraint("CK_Lessons_Status", "[Status] BETWEEN 1 AND 3");
            t.HasCheckConstraint("CK_Lessons_Title", "LEN([Title]) >= 1");
        });
        builder.HasKey(x => x.Id);
        // Principal key for composite tenant foreign keys (participants; attendance, materials later).
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(Lesson.TitleMaxLength).IsRequired();
        builder.Property(x => x.Topic).HasMaxLength(Lesson.TopicMaxLength);
        builder.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MeetingUrl).HasMaxLength(Lesson.MeetingUrlMaxLength);
        builder.Property(x => x.Location).HasMaxLength(Lesson.LocationMaxLength);
        builder.Property(x => x.CancellationReason).HasMaxLength(Lesson.ReasonMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // The calendar reads a workspace's time range; the overlap check reads a teacher's.
        builder.HasIndex(x => new { x.WorkspaceId, x.StartUtc });
        builder.HasIndex(x => new { x.TeacherUserId, x.StartUtc, x.EndUtc });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Lesson.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(Lesson.SubjectId)).OnDelete(DeleteBehavior.NoAction);
        // A group lesson's group belongs to the same workspace (composite tenant FK).
        builder.HasOne(GroupForeignKeyTargets.Group, navigationName: null).WithMany()
            .HasForeignKey(nameof(Lesson.WorkspaceId), nameof(Lesson.GroupId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LessonParticipantConfiguration : IEntityTypeConfiguration<LessonParticipant>
{
    public void Configure(EntityTypeBuilder<LessonParticipant> builder)
    {
        builder.ToTable("LessonParticipants");
        builder.HasKey(x => new { x.LessonId, x.StudentId });
        builder.HasIndex(x => new { x.WorkspaceId, x.StudentId });
        builder.HasTenantReference<LessonParticipant, Lesson>(nameof(LessonParticipant.LessonId));
        // Participants are students of the lesson's workspace (composite tenant FK).
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(LessonParticipant.WorkspaceId), nameof(LessonParticipant.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}
