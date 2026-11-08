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

        lessons.MapPost("/{lessonId:guid}/complete", async (Guid lessonId, LessonVersionRequest request, LessonService service, CancellationToken ct) =>
                TypedResults.Ok(await service.CompleteAsync(lessonId, request, ct)))
            .RequireAuthorization(manage)
            .WithName("CompleteLesson");

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
