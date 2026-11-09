using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Lessons.Application;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
using ClassHub.Core.Modules.Lessons.Jobs;
using ClassHub.Core.Modules.Lessons.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Lessons;

/// <summary>Lessons and the calendar (M07/M08, R2-P4): scheduling without overlaps, held and cancelled lessons.</summary>
public sealed class LessonsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<LessonQueries>();
        services.AddScoped<LessonParticipants>();
        services.AddScoped<LessonService>();
        services.AddScoped<LessonSeriesService>();
        services.AddScoped<BookingService>();
        services.AddScoped<BookingPublicService>();
        services.AddScoped<IBookingIntake>(sp => sp.GetRequiredService<BookingPublicService>());
        services.AddScoped<IBookingAvailability, BookingAvailability>();
        services.AddScoped<ILessonLookup, LessonLookup>();
        services.AddScoped<ILessonEventFeed, LessonEventQueries>();
        services.Configure<LessonOptions>(configuration.GetSection(LessonOptions.SectionName));
        services.AddSingleton<LessonSeriesHorizonJob>();
        services.AddHostedService(sp => sp.GetRequiredService<LessonSeriesHorizonJob>());
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var lessons = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/lessons").WithTags("Lessons").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.LessonsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.LessonsManage);

        lessons.MapGet("", async (DateOnly from, DateOnly to, Guid? studentId, Guid? groupId, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(from, to, studentId, groupId, ct)))
            .RequireAuthorization(read)
            .WithName("ListLessons")
            .WithSummary("Lessons starting on the local days from..to (inclusive, workspace time zone, at most 62 days).");

        lessons.MapGet("/{lessonId:guid}", async (Guid lessonId, LessonService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(lessonId, ct)))
            .RequireAuthorization(read)
            .WithName("GetLesson");

        lessons.MapPost("", async (SaveLessonRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.CreateAsync(request, ct)))
            .WithValidation<SaveLessonRequest>()
            .RequireAuthorization(manage)
            .WithName("CreateLesson")
            .WithSummary("Schedule a lesson; 409 LESSON_OVERLAP when the teacher already has a lesson at that time.");

        lessons.MapPut("/{lessonId:guid}", async (Guid lessonId, SaveLessonRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(lessonId, request, ct)))
            .WithValidation<SaveLessonRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateLesson");

        lessons.MapPost("/{lessonId:guid}/complete", async (Guid lessonId, CompleteLessonRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CompleteAsync(lessonId, request, ct)))
            .WithValidation<CompleteLessonRequest>()
            .RequireAuthorization(manage)
            .WithName("CompleteLesson")
            .WithSummary("Mark as held with attendance (left out: Present). Idempotent: a held lesson is returned as it is.");

        lessons.MapPut("/{lessonId:guid}/attendance", async (Guid lessonId, AttendanceRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAttendanceAsync(lessonId, request, ct)))
            .WithValidation<AttendanceRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateLessonAttendance");

        lessons.MapPut("/{lessonId:guid}/notes", async (Guid lessonId, LessonNotesRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateNotesAsync(lessonId, request, ct)))
            .WithValidation<LessonNotesRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateLessonNotes");

        lessons.MapGet("/history", async (Guid? studentId, Guid? groupId, int? page, int? pageSize, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.HistoryAsync(studentId, groupId, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read)
            .WithName("LessonHistory")
            .WithSummary("A student's or a group's lessons, newest first.");

        lessons.MapPost("/{lessonId:guid}/cancel", async (Guid lessonId, CancelLessonRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CancelAsync(lessonId, request, ct)))
            .WithValidation<CancelLessonRequest>()
            .RequireAuthorization(manage)
            .WithName("CancelLesson");

        // Recurring lessons (R2-P5, D-24).
        var series = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/lesson-series").WithTags("Lessons").RequireAntiforgeryHeader();
        series.MapPost("", async (SaveSeriesRequest request, LessonSeriesService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.CreateAsync(request, ct)))
            .WithValidation<SaveSeriesRequest>()
            .RequireAuthorization(manage)
            .WithName("CreateLessonSeries")
            .WithSummary("Set up weekly or bi-weekly lessons; occurrences are created 12 weeks ahead and conflicts are reported per occurrence.");

        series.MapGet("/{seriesId:guid}", async (Guid seriesId, LessonSeriesService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(seriesId, ct)))
            .RequireAuthorization(read)
            .WithName("GetLessonSeries");

        series.MapPut("/{seriesId:guid}", async (Guid seriesId, UpdateSeriesRequest request, LessonSeriesService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(seriesId, request, ct)))
            .WithValidation<UpdateSeriesRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateLessonSeries")
            .WithSummary("Change this and the following lessons, or all future ones (D-24); a new series continues.");

        series.MapPost("/{seriesId:guid}/end", async (Guid seriesId, EndSeriesRequest request, LessonSeriesService service, CancellationToken ct) =>
                TypedResults.Ok(await service.EndAsync(seriesId, request, ct)))
            .WithValidation<EndSeriesRequest>()
            .RequireAuthorization(manage)
            .WithName("EndLessonSeries");

        // Self-service booking (R2-P6): the teacher's side.
        var booking = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/booking").WithTags("Booking").RequireAntiforgeryHeader();
        booking.MapGet("/settings", async (BookingService service, CancellationToken ct) => TypedResults.Ok(await service.GetSettingsAsync(ct)))
            .RequireAuthorization(read)
            .WithName("GetBookingSettings");
        booking.MapPut("/settings", async (SaveBookingSettingsRequest request, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SaveSettingsAsync(request, ct)))
            .WithValidation<SaveBookingSettingsRequest>()
            .RequireAuthorization(manage)
            .WithName("SaveBookingSettings");
        booking.MapGet("/requests", async (string? status, int? page, int? pageSize, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(status, page ?? 1, pageSize ?? BookingService.DefaultPageSize, ct)))
            .RequireAuthorization(read)
            .WithName("ListBookingRequests");
        booking.MapGet("/requests/{bookingId:guid}", async (Guid bookingId, BookingService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(bookingId, ct)))
            .RequireAuthorization(read)
            .WithName("GetBookingRequest");
        booking.MapPost("/requests/{bookingId:guid}/confirm", async (Guid bookingId, BookingDecisionRequest request, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ConfirmAsync(bookingId, request, ct)))
            .RequireAuthorization(manage)
            .WithName("ConfirmBookingRequest")
            .WithSummary("Creates the student (if needed) and the lesson after a conflict check; 409 LESSON_OVERLAP leaves it pending.");
        booking.MapPost("/requests/{bookingId:guid}/decline", async (Guid bookingId, BookingDecisionRequest request, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.DeclineAsync(bookingId, request, ct)))
            .WithValidation<BookingDecisionRequest>()
            .RequireAuthorization(manage)
            .WithName("DeclineBookingRequest");
        booking.MapPost("/requests/{bookingId:guid}/reschedule", async (Guid bookingId, RescheduleBookingRequest request, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RescheduleAsync(bookingId, request, ct)))
            .WithValidation<RescheduleBookingRequest>()
            .RequireAuthorization(manage)
            .WithName("RescheduleBookingRequest");
        booking.MapPost("/requests/{bookingId:guid}/cancel", async (Guid bookingId, CancelBookingRequest request, BookingService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CancelAsync(bookingId, request, ct)))
            .WithValidation<CancelBookingRequest>()
            .RequireAuthorization(manage)
            .WithName("CancelBookingRequest");

        // The visitor's side (anonymous): bookable offers and free slots of a published site.
        var site = endpoints.MapGroup("/public/sites/{slug:siteslug}/booking").WithTags("Public booking").AllowAnonymous();
        site.MapGet("", async (string slug, BookingPublicService service, CancellationToken ct) => TypedResults.Ok(await service.GetPageAsync(slug, ct)))
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetPublicBooking");
        site.MapGet("/slots", async (string slug, Guid offerId, DateOnly from, BookingPublicService service, CancellationToken ct) =>
                TypedResults.Ok(await service.GetSlotsAsync(slug, offerId, from, ct)))
            .RequireRateLimiting(RateLimitPolicies.PublicRead)
            .WithName("GetPublicBookingSlots");
    }
}

internal sealed class SaveLessonRequestValidator : AbstractValidator<SaveLessonRequest>
{
    public SaveLessonRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().IsEnumName(typeof(LessonKind), caseSensitive: true);
        RuleFor(x => x.StudentId).NotNull().When(x => x.Kind == nameof(LessonKind.Individual)).WithMessage("Choose the student.");
        RuleFor(x => x.GroupId).NotNull().When(x => x.Kind == nameof(LessonKind.Group)).WithMessage("Choose the group.");
        RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= Lesson.TitleMaxLength)
            .WithMessage($"Enter a title (up to {Lesson.TitleMaxLength} characters).");
        RuleFor(x => x.Topic).MaximumLength(Lesson.TopicMaxLength);
        RuleFor(x => x.StartLocal).NotNull();
        RuleFor(x => x.DurationMinutes).NotNull().InclusiveBetween(Lesson.MinDurationMinutes, Lesson.MaxDurationMinutes);
        RuleFor(x => x.Mode).NotNull().IsEnumName(typeof(LessonMode), caseSensitive: true);
        RuleFor(x => x.MeetingUrl).MaximumLength(Lesson.MeetingUrlMaxLength)
            .Must(u => Uri.TryCreate(u!.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrWhiteSpace(x.MeetingUrl))
            .WithMessage("Enter an https:// link.");
        RuleFor(x => x.Location).MaximumLength(Lesson.LocationMaxLength);
    }
}

internal sealed class CancelLessonRequestValidator : AbstractValidator<CancelLessonRequest>
{
    public CancelLessonRequestValidator() =>
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 1 and <= Lesson.ReasonMaxLength)
            .WithMessage($"Give a reason (up to {Lesson.ReasonMaxLength} characters).");
}

internal sealed class SaveSeriesRequestValidator : AbstractValidator<SaveSeriesRequest>
{
    public SaveSeriesRequestValidator()
    {
        RuleFor(x => x.Kind).NotNull().IsEnumName(typeof(LessonKind), caseSensitive: true);
        RuleFor(x => x.StudentId).NotNull().When(x => x.Kind == nameof(LessonKind.Individual)).WithMessage("Choose the student.");
        RuleFor(x => x.GroupId).NotNull().When(x => x.Kind == nameof(LessonKind.Group)).WithMessage("Choose the group.");
        RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= Lesson.TitleMaxLength)
            .WithMessage($"Enter a title (up to {Lesson.TitleMaxLength} characters).");
        RuleFor(x => x.Topic).MaximumLength(Lesson.TopicMaxLength);
        RuleFor(x => x.StartDate).NotNull();
        RuleFor(x => x.StartTime).NotNull();
        RuleFor(x => x.DurationMinutes).NotNull().InclusiveBetween(Lesson.MinDurationMinutes, Lesson.MaxDurationMinutes);
        RuleFor(x => x.IntervalWeeks).NotNull().InclusiveBetween(1, 2);
        RuleFor(x => x.Count).InclusiveBetween(1, LessonSeries.MaxCount);
        RuleFor(x => x).Must(x => x.Count is null || x.EndDate is null).OverridePropertyName("endDate").WithMessage("End either on a date or after a number of lessons.");
        RuleFor(x => x.EndDate).Must((x, end) => end is null || x.StartDate is null || (end >= x.StartDate && end.Value.DayNumber - x.StartDate.Value.DayNumber <= LessonSeries.MaxSpanDays))
            .WithMessage("The end date must be on or after the start and within two years.");
        RuleFor(x => x.Mode).NotNull().IsEnumName(typeof(LessonMode), caseSensitive: true);
        RuleFor(x => x.MeetingUrl).MaximumLength(Lesson.MeetingUrlMaxLength)
            .Must(u => Uri.TryCreate(u!.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
            .When(x => !string.IsNullOrWhiteSpace(x.MeetingUrl))
            .WithMessage("Enter an https:// link.");
        RuleFor(x => x.Location).MaximumLength(Lesson.LocationMaxLength);
    }
}

internal sealed class UpdateSeriesRequestValidator : AbstractValidator<UpdateSeriesRequest>
{
    public UpdateSeriesRequestValidator()
    {
        RuleFor(x => x.Scope).Must(s => s is LessonSeriesService.ThisAndFollowing or LessonSeriesService.AllFuture)
            .WithMessage("Choose ThisAndFollowing or AllFuture.");
        RuleFor(x => x.FromLessonId).NotNull().When(x => x.Scope == LessonSeriesService.ThisAndFollowing);
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Series).NotNull().SetValidator(new SaveSeriesRequestValidator()!);
    }
}

internal sealed class EndSeriesRequestValidator : AbstractValidator<EndSeriesRequest>
{
    public EndSeriesRequestValidator()
    {
        RuleFor(x => x.FromDate).NotNull();
        RuleFor(x => x.Version).NotNull();
    }
}

internal sealed class SaveBookingSettingsRequestValidator : AbstractValidator<SaveBookingSettingsRequest>
{
    public SaveBookingSettingsRequestValidator()
    {
        // Null-safe throughout: a missing or null field is a validation error, never a server error.
        RuleFor(x => x.SlotStepMinutes).Cascade(CascadeMode.Stop).NotNull().Must(s => BookingSettings.SlotSteps.Contains(s!.Value)).WithMessage("Choose 15, 30 or 60 minutes.");
        RuleFor(x => x.MinNoticeHours).Cascade(CascadeMode.Stop).NotNull().InclusiveBetween(0, BookingSettings.MaxNoticeHours);
        RuleFor(x => x.HorizonDays).Cascade(CascadeMode.Stop).NotNull().InclusiveBetween(BookingSettings.MinHorizonDays, BookingSettings.MaxHorizonDays);
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.WorkingHours).Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(h => h!.Count <= 42).WithMessage("At most 42 intervals.")
            .Must(h => h!.All(i => i is not null && i.End > i.Start && Enum.IsDefined(i.DayOfWeek))).WithMessage("Each interval ends after it starts.")
            .Must(h => !h!.GroupBy(i => i.DayOfWeek).Any(day => day.OrderBy(i => i.Start).Zip(day.OrderBy(i => i.Start).Skip(1)).Any(p => p.Second.Start < p.First.End)))
            .WithMessage("Intervals of one day must not overlap.");
        RuleFor(x => x.TimeOff).Cascade(CascadeMode.Stop)
            .NotNull()
            .Must(o => o!.Count <= 100).WithMessage("At most 100 periods.")
            .Must(o => o!.All(p => p is not null && p.EndDate >= p.StartDate && (p.Note?.Length ?? 0) <= TimeOff.NoteMaxLength))
            .WithMessage("Each period ends on or after its start (note up to 120 characters).");
    }
}

internal sealed class BookingDecisionRequestValidator : AbstractValidator<BookingDecisionRequest>
{
    public BookingDecisionRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Reason).MaximumLength(BookingRequest.ReasonMaxLength);
    }
}

internal sealed class CancelBookingRequestValidator : AbstractValidator<CancelBookingRequest>
{
    public CancelBookingRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Reason).Must(r => r is not null && r.Trim().Length is >= 1 and <= BookingRequest.ReasonMaxLength)
            .WithMessage($"Give a reason (up to {BookingRequest.ReasonMaxLength} characters).");
    }
}

internal sealed class RescheduleBookingRequestValidator : AbstractValidator<RescheduleBookingRequest>
{
    public RescheduleBookingRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.StartLocal).NotNull();
    }
}

internal sealed class AttendanceItemValidator : AbstractValidator<AttendanceItem>
{
    public AttendanceItemValidator() => RuleFor(x => x.Status).Cascade(CascadeMode.Stop).NotNull().IsEnumName(typeof(AttendanceStatus), caseSensitive: true);
}

internal sealed class CompleteLessonRequestValidator : AbstractValidator<CompleteLessonRequest>
{
    public CompleteLessonRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Attendance).Must(a => a is null || (a.Count <= 200 && a.All(i => i is not null))).WithMessage("At most 200 entries.");
        RuleForEach(x => x.Attendance).SetValidator(new AttendanceItemValidator()).When(x => x.Attendance is not null && x.Attendance.All(i => i is not null));
    }
}

internal sealed class AttendanceRequestValidator : AbstractValidator<AttendanceRequest>
{
    public AttendanceRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.Attendance).Cascade(CascadeMode.Stop).NotNull().Must(a => a!.Count is >= 1 and <= 200 && a.All(i => i is not null)).WithMessage("Give 1 to 200 entries.");
        RuleForEach(x => x.Attendance).SetValidator(new AttendanceItemValidator()).When(x => x.Attendance is not null && x.Attendance.All(i => i is not null));
    }
}

internal sealed class LessonNotesRequestValidator : AbstractValidator<LessonNotesRequest>
{
    public LessonNotesRequestValidator()
    {
        RuleFor(x => x.Version).NotNull();
        RuleFor(x => x.PrivateNote).MaximumLength(Lesson.PrivateNoteMaxLength);
        RuleFor(x => x.SharedSummary).MaximumLength(Lesson.SharedSummaryMaxLength);
    }
}
