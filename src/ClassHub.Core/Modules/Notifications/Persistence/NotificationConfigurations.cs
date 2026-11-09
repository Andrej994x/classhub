using ClassHub.Core.Modules.Identity.Contracts;
using ClassHub.Core.Modules.Notifications.Domain;
using ClassHub.Core.Modules.Students.Contracts;
using ClassHub.Core.Modules.Workspaces.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ClassHub.Core.Modules.Notifications.Persistence;

internal sealed class NotificationConfiguration : IEntityTypeConfiguration<Notification>
{
    public void Configure(EntityTypeBuilder<Notification> builder)
    {
        builder.ToTable("Notifications", t =>
        {
            t.HasCheckConstraint("CK_Notifications_Kind", "[Kind] BETWEEN 1 AND 6");
            t.HasCheckConstraint("CK_Notifications_Status", "[Status] BETWEEN 1 AND 5");
            t.HasCheckConstraint("CK_Notifications_Subject",
                "([Kind] = 5 AND [HomeworkId] IS NOT NULL AND [HomeworkAssignmentId] IS NOT NULL AND [LessonId] IS NULL) OR ([Kind] <> 5 AND [LessonId] IS NOT NULL AND [HomeworkId] IS NULL AND [HomeworkAssignmentId] IS NULL)");
            t.HasCheckConstraint("CK_Notifications_Series", "([Kind] = 6 AND [SeriesId] IS NOT NULL) OR ([Kind] <> 6 AND [SeriesId] IS NULL)");
            t.HasCheckConstraint("CK_Notifications_Sent", "([Status] = 2 AND [SentAtUtc] IS NOT NULL) OR ([Status] <> 2 AND [SentAtUtc] IS NULL)");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.DedupeKey).HasMaxLength(Notification.DedupeKeyMaxLength).IsUnicode(false).IsRequired();
        builder.Property(x => x.Outcome).HasMaxLength(Notification.OutcomeMaxLength);
        builder.Property(x => x.LessonTitle).HasMaxLength(Notification.TitleMaxLength);
        builder.Property(x => x.LessonTimeZone).HasMaxLength(Notification.TimeZoneMaxLength).IsUnicode(false);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // Never two of the same notification.
        builder.HasIndex(x => x.DedupeKey).IsUnique();
        // The dispatcher's claim: pending and due.
        builder.HasIndex(x => new { x.Status, x.DueAtUtc });
        builder.HasIndex(x => new { x.WorkspaceId, x.LessonId, x.StudentId });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(Notification.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(Notification.WorkspaceId), nameof(Notification.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class NotificationPreferenceConfiguration : IEntityTypeConfiguration<NotificationPreference>
{
    public void Configure(EntityTypeBuilder<NotificationPreference> builder)
    {
        builder.ToTable("NotificationPreferences");
        builder.HasKey(x => x.UserId);
        builder.HasOne<AppUser>().WithOne().HasForeignKey<NotificationPreference>(x => x.UserId).OnDelete(DeleteBehavior.NoAction);
    }
}
