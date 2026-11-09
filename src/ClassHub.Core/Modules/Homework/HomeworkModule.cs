using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Homework.Application;
using ClassHub.Core.Modules.Homework.Contracts;
using ClassHub.Core.Modules.Homework.Domain;
using ClassHub.Core.Modules.Homework.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Homework;

/// <summary>Homework (M10, R2-P9): tasks for students, their answers with files, and the teacher's review.</summary>
public sealed class HomeworkModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<HomeworkQueries>();
        services.AddScoped<HomeworkService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var homework = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/homework").WithTags("Homework").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.HomeworkRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.HomeworkManage);

        homework.MapGet("", async (string? q, Guid? lessonId, bool? archived, int? page, int? pageSize, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(q, lessonId, archived ?? false, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read)
            .WithName("ListHomework")
            .WithSummary("Homework (active or archived), newest change first, with the number of assigned, submitted and reviewed students; optionally of one lesson.");

        homework.MapGet("/{homeworkId:guid}", async (Guid homeworkId, HomeworkService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(homeworkId, ct)))
            .RequireAuthorization(read)
            .WithName("GetHomework");

        homework.MapPost("", async Task<Created<HomeworkResponse>> (SaveHomeworkRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.CreateAsync(request, ct)))
            .WithValidation<SaveHomeworkRequest>()
            .RequireAuthorization(manage)
            .WithName("CreateHomework");

        homework.MapPut("/{homeworkId:guid}", async (Guid homeworkId, SaveHomeworkRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(homeworkId, request, ct)))
            .WithValidation<SaveHomeworkRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateHomework");

        homework.MapPost("/{homeworkId:guid}/archive", async (Guid homeworkId, HomeworkVersionRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(homeworkId, request, archived: true, ct)))
            .RequireAuthorization(manage)
            .WithName("ArchiveHomework");

        homework.MapPost("/{homeworkId:guid}/restore", async (Guid homeworkId, HomeworkVersionRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(homeworkId, request, archived: false, ct)))
            .RequireAuthorization(manage)
            .WithName("RestoreHomework");

        homework.MapPost("/{homeworkId:guid}/assignments", async (Guid homeworkId, AssignHomeworkRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AssignAsync(homeworkId, request, ct)))
            .WithValidation<AssignHomeworkRequest>()
            .RequireAuthorization(manage)
            .WithName("AssignHomework");

        homework.MapDelete("/{homeworkId:guid}/assignments/{assignmentId:guid}", async (Guid homeworkId, Guid assignmentId, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UnassignAsync(homeworkId, assignmentId, ct)))
            .RequireAuthorization(manage)
            .WithName("UnassignHomework")
            .WithSummary("Only while the student has not answered.");

        homework.MapPost("/{homeworkId:guid}/assignments/{assignmentId:guid}/review", async (Guid homeworkId, Guid assignmentId, ReviewHomeworkRequest request, HomeworkService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ReviewAsync(homeworkId, assignmentId, request, ct)))
            .WithValidation<ReviewHomeworkRequest>()
            .RequireAuthorization(manage)
            .WithName("ReviewHomework");

        homework.MapGet("/{homeworkId:guid}/assignments/{assignmentId:guid}/file", async (Guid homeworkId, Guid assignmentId, HttpContext http, HomeworkService service, CancellationToken ct) =>
                DocumentUploads.FileResult(http, await service.OpenAnswerAsync(homeworkId, assignmentId, ct)))
            .RequireAuthorization(read)
            .WithName("DownloadHomeworkAnswer");

        // The student portal: only the student's own assignments.
        var portal = endpoints.MapGroup("/student/w/{workspaceId:guid}/homework").WithTags("Student portal").RequireAntiforgeryHeader();
        portal.MapGet("", async (HomeworkService service, CancellationToken ct) => TypedResults.Ok(await service.ListForStudentAsync(ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("ListMyHomework");
        portal.MapGet("/{assignmentId:guid}", async (Guid assignmentId, HomeworkService service, CancellationToken ct) => TypedResults.Ok(await service.GetForStudentAsync(assignmentId, ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("GetMyHomework");

        // multipart/form-data: "text", optional "file", "removeFile" ("true" drops the current file).
        portal.MapPost("/{assignmentId:guid}/submission", async (Guid assignmentId, HttpContext http, HomeworkService service, CancellationToken ct) =>
            {
                var (form, fileName, content) = await DocumentUploads.ReadAsync(http, fileRequired: false, ct);
                var removeFile = string.Equals(form["removeFile"].ToString(), "true", StringComparison.OrdinalIgnoreCase);
                return TypedResults.Ok(await service.SubmitAsync(assignmentId, form["text"].ToString(), removeFile, fileName, content, ct));
            })
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("SubmitMyHomework")
            .WithSummary("Answer with text and/or a file (PDF, PNG, JPEG, WebP, DOCX, XLSX, PPTX or TXT, at most 20 MB); allowed until reviewed.");
        portal.MapGet("/{assignmentId:guid}/file", async (Guid assignmentId, HttpContext http, HomeworkService service, CancellationToken ct) =>
                DocumentUploads.FileResult(http, await service.OpenOwnAnswerAsync(assignmentId, ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("DownloadMyHomeworkAnswer");
    }
}

internal sealed class SaveHomeworkRequestValidator : AbstractValidator<SaveHomeworkRequest>
{
    public SaveHomeworkRequestValidator()
    {
        RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= HomeworkTask.TitleMaxLength)
            .WithMessage($"Enter a title (up to {HomeworkTask.TitleMaxLength} characters).");
        RuleFor(x => x.Description).MaximumLength(HomeworkTask.DescriptionMaxLength);
        RuleFor(x => x.DueDate).Must(d => d is null || (d.Value.Year is >= 2000 and <= 2100)).WithMessage("Enter a valid due date.");
    }
}

internal sealed class AssignHomeworkRequestValidator : AbstractValidator<AssignHomeworkRequest>
{
    public AssignHomeworkRequestValidator()
    {
        RuleFor(x => x).Must(x => (x.StudentIds?.Count ?? 0) > 0 || x.GroupId is not null).OverridePropertyName("studentIds").WithMessage("Choose students or a group.");
        RuleFor(x => x.StudentIds).Must(ids => ids is null || ids.Count <= 200).WithMessage("Up to 200 students at once.");
    }
}

internal sealed class ReviewHomeworkRequestValidator : AbstractValidator<ReviewHomeworkRequest>
{
    public ReviewHomeworkRequestValidator()
    {
        RuleFor(x => x.Comment).MaximumLength(HomeworkAssignment.CommentMaxLength);
        RuleFor(x => x.Version).NotNull();
    }
}
