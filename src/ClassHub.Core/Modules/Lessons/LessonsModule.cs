using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Lessons.Application;
using ClassHub.Core.Modules.Lessons.Contracts;
using ClassHub.Core.Modules.Lessons.Domain;
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
        services.AddScoped<LessonService>();
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
