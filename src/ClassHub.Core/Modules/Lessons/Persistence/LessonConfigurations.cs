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
        builder.Property(x => x.PrivateNote).HasMaxLength(Lesson.PrivateNoteMaxLength);
        builder.Property(x => x.SharedSummary).HasMaxLength(Lesson.SharedSummaryMaxLength);
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
        // An occurrence belongs to a series of the same workspace and is created once per index.
        builder.HasOne<LessonSeries>().WithMany()
            .HasForeignKey(nameof(Lesson.WorkspaceId), nameof(Lesson.SeriesId))
            .HasPrincipalKey(nameof(LessonSeries.WorkspaceId), nameof(LessonSeries.Id))
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(x => new { x.SeriesId, x.SeriesIndex }).IsUnique().HasFilter("[SeriesId] IS NOT NULL");
    }
}

internal sealed class LessonSeriesConfiguration : IEntityTypeConfiguration<LessonSeries>
{
    public void Configure(EntityTypeBuilder<LessonSeries> builder)
    {
        builder.ToTable("LessonSeries", t =>
        {
            t.HasCheckConstraint("CK_LessonSeries_Kind", "([Kind] = 1 AND [StudentId] IS NOT NULL AND [GroupId] IS NULL) OR ([Kind] = 2 AND [GroupId] IS NOT NULL AND [StudentId] IS NULL)");
            t.HasCheckConstraint("CK_LessonSeries_Mode", "[Mode] IN (1, 2)");
            t.HasCheckConstraint("CK_LessonSeries_Interval", "[IntervalWeeks] IN (1, 2)");
            t.HasCheckConstraint("CK_LessonSeries_Duration", "[DurationMinutes] BETWEEN 15 AND 480");
            t.HasCheckConstraint("CK_LessonSeries_Count", "[Count] IS NULL OR [Count] BETWEEN 1 AND 200");
            t.HasCheckConstraint("CK_LessonSeries_Title", "LEN([Title]) >= 1");
        });
        builder.HasKey(x => x.Id);
        builder.HasAlternateKey(x => new { x.WorkspaceId, x.Id });
        builder.Property(x => x.Title).HasMaxLength(Lesson.TitleMaxLength).IsRequired();
        builder.Property(x => x.Topic).HasMaxLength(Lesson.TopicMaxLength);
        builder.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.MeetingUrl).HasMaxLength(Lesson.MeetingUrlMaxLength);
        builder.Property(x => x.Location).HasMaxLength(Lesson.LocationMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        // The horizon job looks for series that may still need occurrences.
        builder.HasIndex(x => new { x.ReplacedBySeriesId, x.EndDate });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(LessonSeries.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<AppUser>().WithMany().HasForeignKey(x => x.TeacherUserId).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(CatalogForeignKeyTargets.Subject, navigationName: null).WithMany().HasForeignKey(nameof(LessonSeries.SubjectId)).OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(StudentForeignKeyTargets.Student, navigationName: null).WithMany()
            .HasForeignKey(nameof(LessonSeries.WorkspaceId), nameof(LessonSeries.StudentId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(GroupForeignKeyTargets.Group, navigationName: null).WithMany()
            .HasForeignKey(nameof(LessonSeries.WorkspaceId), nameof(LessonSeries.GroupId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<LessonSeries>().WithMany()
            .HasForeignKey(nameof(LessonSeries.WorkspaceId), nameof(LessonSeries.ReplacedBySeriesId))
            .HasPrincipalKey(nameof(LessonSeries.WorkspaceId), nameof(LessonSeries.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LessonSeriesSkipConfiguration : IEntityTypeConfiguration<LessonSeriesSkip>
{
    public void Configure(EntityTypeBuilder<LessonSeriesSkip> builder)
    {
        builder.ToTable("LessonSeriesSkips", t => t.HasCheckConstraint("CK_LessonSeriesSkips_Reason", "[Reason] BETWEEN 1 AND 3"));
        builder.HasKey(x => new { x.SeriesId, x.SeriesIndex });
        builder.HasTenantReference<LessonSeriesSkip, LessonSeries>(nameof(LessonSeriesSkip.SeriesId));
    }
}

internal sealed class LessonParticipantConfiguration : IEntityTypeConfiguration<LessonParticipant>
{
    public void Configure(EntityTypeBuilder<LessonParticipant> builder)
    {
        builder.ToTable("LessonParticipants", t => t.HasCheckConstraint("CK_LessonParticipants_Attendance", "[Attendance] IS NULL OR [Attendance] BETWEEN 1 AND 3"));
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

internal sealed class BookingSettingsConfiguration : IEntityTypeConfiguration<BookingSettings>
{
    public void Configure(EntityTypeBuilder<BookingSettings> builder)
    {
        builder.ToTable("BookingSettings", t =>
        {
            t.HasCheckConstraint("CK_BookingSettings_Step", "[SlotStepMinutes] IN (15, 30, 60)");
            t.HasCheckConstraint("CK_BookingSettings_Notice", "[MinNoticeHours] BETWEEN 0 AND 168");
            t.HasCheckConstraint("CK_BookingSettings_Horizon", "[HorizonDays] BETWEEN 7 AND 90");
        });
        builder.HasKey(x => x.WorkspaceId);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(BookingSettings.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("WorkingHours", t =>
        {
            t.HasCheckConstraint("CK_WorkingHours_Day", "[DayOfWeek] BETWEEN 0 AND 6");
            t.HasCheckConstraint("CK_WorkingHours_Time", "[EndTime] > [StartTime]");
        });
        builder.HasKey(x => x.Id);
        builder.HasIndex(x => new { x.WorkspaceId, x.DayOfWeek });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(WorkingHours.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> builder)
    {
        builder.ToTable("TimeOff", t => t.HasCheckConstraint("CK_TimeOff_Dates", "[EndDate] >= [StartDate]"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Note).HasMaxLength(TimeOff.NoteMaxLength);
        builder.HasIndex(x => new { x.WorkspaceId, x.StartDate });
        builder.HasOne(WorkspaceForeignKeyTargets.Workspace, navigationName: null).WithMany().HasForeignKey(nameof(TimeOff.WorkspaceId)).OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class BookingRequestConfiguration : IEntityTypeConfiguration<BookingRequest>
{
    public void Configure(EntityTypeBuilder<BookingRequest> builder)
    {
        builder.ToTable("BookingRequests", t =>
        {
            t.HasCheckConstraint("CK_BookingRequests_Time", "[EndUtc] > [StartUtc]");
            t.HasCheckConstraint("CK_BookingRequests_Status", "[Status] BETWEEN 1 AND 4");
            t.HasCheckConstraint("CK_BookingRequests_Mode", "[Mode] IN (1, 2)");
            t.HasCheckConstraint("CK_BookingRequests_Lesson", "([Status] IN (2, 4)) OR [LessonId] IS NULL");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.OfferTitle).HasMaxLength(200).IsRequired();
        builder.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Reason).HasMaxLength(BookingRequest.ReasonMaxLength);
        builder.Property(x => x.Version).IsConcurrencyToken();
        builder.HasIndex(x => new { x.WorkspaceId, x.Status, x.StartUtc });
        builder.HasIndex(x => x.LeadId).IsUnique();
        builder.HasIndex(x => x.LessonId).IsUnique().HasFilter("[LessonId] IS NOT NULL");
        // The request, the offer and the lesson belong to the same workspace (composite tenant FKs).
        builder.HasOne(ClassHub.Core.Modules.Leads.Contracts.LeadForeignKeyTargets.Lead, navigationName: null).WithMany()
            .HasForeignKey(nameof(BookingRequest.WorkspaceId), nameof(BookingRequest.LeadId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne(ClassHub.Core.Modules.Offers.Contracts.OfferForeignKeyTargets.Offer, navigationName: null).WithMany()
            .HasForeignKey(nameof(BookingRequest.WorkspaceId), nameof(BookingRequest.OfferId))
            .HasPrincipalKey("WorkspaceId", "Id")
            .OnDelete(DeleteBehavior.NoAction);
        builder.HasOne<Lesson>().WithMany()
            .HasForeignKey(nameof(BookingRequest.WorkspaceId), nameof(BookingRequest.LessonId))
            .HasPrincipalKey(nameof(Lesson.WorkspaceId), nameof(Lesson.Id))
            .OnDelete(DeleteBehavior.NoAction);
    }
}

internal sealed class LessonEventConfiguration : IEntityTypeConfiguration<LessonEvent>
{
    public void Configure(EntityTypeBuilder<LessonEvent> builder)
    {
        builder.ToTable("LessonEvents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Kind).HasMaxLength(40).IsRequired();
        builder.Property(x => x.PayloadJson).IsRequired();
        // A lesson is completed once: at most one completion event, whatever races.
        builder.HasIndex(x => new { x.LessonId, x.Kind }).IsUnique().HasFilter("[Kind] = N'LessonCompleted'");
        builder.HasIndex(x => new { x.ProcessedAtUtc, x.OccurredAtUtc });
        builder.HasTenantReference<LessonEvent, Lesson>(nameof(LessonEvent.LessonId));
    }
}
