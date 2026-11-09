using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Materials.Application;
using ClassHub.Core.Modules.Materials.Contracts;
using ClassHub.Core.Modules.Materials.Domain;
using ClassHub.Core.Modules.Materials.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Core.Modules.Materials;

/// <summary>Materials (M10, R2-P8): private teaching files, assigned to students or groups and opened in the portal.</summary>
public sealed class MaterialsModule : IModule
{
    /// <summary>Upper bound for the upload request (the file itself is limited to 20 MB).</summary>
    private const long UploadRequestLimitBytes = MaterialFiles.MaxBytes + (256 * 1024);

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<MaterialQueries>();
        services.AddScoped<MaterialService>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var materials = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/materials").WithTags("Materials").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.MaterialsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.MaterialsManage);

        materials.MapGet("", async (string? q, Guid? subjectId, bool? archived, int? page, int? pageSize, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(q, subjectId, archived ?? false, page ?? 1, pageSize ?? 20, ct)))
            .RequireAuthorization(read)
            .WithName("ListMaterials")
            .WithSummary("Materials (active or archived), newest change first; with the storage the workspace uses.");

        materials.MapGet("/{materialId:guid}", async (Guid materialId, MaterialService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(materialId, ct)))
            .RequireAuthorization(read)
            .WithName("GetMaterial");

        // multipart/form-data: "file" plus "title", "description", "subjectId". Read manually (not IFormFile binding): the
        // antiforgery header filter protects it like every other unsafe request.
        materials.MapPost("", async Task<Created<MaterialResponse>> (HttpContext http, MaterialService service, CancellationToken ct) =>
            {
                var form = await ReadFormAsync(http, ct);
                var file = form.Files.GetFile("file") ?? throw new BadRequestException("The form has no file part named 'file'.", ErrorCodes.FileNotAllowed);
                if (file.Length > MaterialFiles.MaxBytes)
                {
                    throw new BadRequestException("The file must be at most 20 MB.", ErrorCodes.PayloadTooLarge);
                }

                Guid? subjectId = null;
                if (form["subjectId"].ToString() is { Length: > 0 } subject)
                {
                    subjectId = Guid.TryParse(subject, out var parsed)
                        ? parsed
                        : throw new RequestValidationException(new Dictionary<string, string[]>(StringComparer.Ordinal) { ["subjectId"] = ["Unknown subject."] });
                }

                using var buffer = new MemoryStream((int)file.Length);
                await file.CopyToAsync(buffer, ct);
                return TypedResults.Created((string?)null, await service.CreateAsync(form["title"].ToString(), form["description"].ToString(), subjectId, file.FileName, buffer.ToArray(), ct));
            })
            .RequireAuthorization(manage)
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("CreateMaterial")
            .WithSummary("Upload a material (PDF, PNG, JPEG, WebP, DOCX, XLSX, PPTX or TXT, at most 20 MB); counts towards the plan storage.");

        materials.MapPut("/{materialId:guid}", async (Guid materialId, UpdateMaterialRequest request, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(materialId, request, ct)))
            .WithValidation<UpdateMaterialRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateMaterial");

        materials.MapPost("/{materialId:guid}/archive", async (Guid materialId, MaterialVersionRequest request, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(materialId, request, archived: true, ct)))
            .RequireAuthorization(manage)
            .WithName("ArchiveMaterial");

        materials.MapPost("/{materialId:guid}/restore", async (Guid materialId, MaterialVersionRequest request, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(materialId, request, archived: false, ct)))
            .RequireAuthorization(manage)
            .WithName("RestoreMaterial");

        materials.MapPost("/{materialId:guid}/delete", async (Guid materialId, MaterialVersionRequest request, MaterialService service, CancellationToken ct) =>
            {
                await service.DeleteAsync(materialId, request, ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(manage)
            .WithName("DeleteMaterial")
            .WithSummary("Delete the material, its assignments and its file (frees the storage).");

        materials.MapPost("/{materialId:guid}/assignments", async (Guid materialId, AssignMaterialRequest request, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AssignAsync(materialId, request, ct)))
            .WithValidation<AssignMaterialRequest>()
            .RequireAuthorization(manage)
            .WithName("AssignMaterial");

        materials.MapDelete("/{materialId:guid}/assignments/{assignmentId:guid}", async (Guid materialId, Guid assignmentId, MaterialService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UnassignAsync(materialId, assignmentId, ct)))
            .RequireAuthorization(manage)
            .WithName("UnassignMaterial");

        materials.MapGet("/{materialId:guid}/file", async (Guid materialId, HttpContext http, MaterialService service, CancellationToken ct) =>
                FileResult(http, await service.OpenAsync(materialId, ct)))
            .RequireAuthorization(read)
            .WithName("DownloadMaterial");

        // The student portal: only the student's own (assigned) materials.
        var portal = endpoints.MapGroup("/student/w/{workspaceId:guid}/materials").WithTags("Student portal").RequireAntiforgeryHeader();
        portal.MapGet("", async (MaterialService service, CancellationToken ct) => TypedResults.Ok(await service.ListForStudentAsync(ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("ListMyMaterials");
        portal.MapGet("/{materialId:guid}/file", async (Guid materialId, HttpContext http, MaterialService service, CancellationToken ct) =>
                FileResult(http, await service.OpenForStudentAsync(materialId, ct)))
            .RequireAuthorization(ClassHubPolicies.StudentSelf)
            .WithName("DownloadMyMaterial")
            .WithSummary("Only a material assigned to the student or to one of their groups; anything else is 404.");
    }

    private static async Task<IFormCollection> ReadFormAsync(HttpContext http, CancellationToken cancellationToken)
    {
        var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (sizeLimit is { IsReadOnly: false })
        {
            sizeLimit.MaxRequestBodySize = UploadRequestLimitBytes;
        }

        if (!http.Request.HasFormContentType)
        {
            throw new BadRequestException("Send the file as multipart/form-data.", ErrorCodes.UnsupportedMediaType);
        }

        try
        {
            return await http.Request.ReadFormAsync(new FormOptions { MultipartBodyLengthLimit = UploadRequestLimitBytes }, cancellationToken);
        }
        catch (InvalidDataException)
        {
            throw new BadRequestException("The upload is not valid multipart/form-data.", ErrorCodes.BadRequest);
        }
    }

    /// <summary>
    /// A material file: never sniffed, sandboxed, not cached by shared caches, and downloaded (as an attachment with the
    /// safe name) unless it is an image.
    /// </summary>
    private static FileStreamHttpResult FileResult(HttpContext http, MaterialFile file)
    {
        http.Response.Headers[HeaderNames.XContentTypeOptions] = "nosniff";
        http.Response.Headers[HeaderNames.ContentSecurityPolicy] = "sandbox; default-src 'none'; img-src 'self'";
        http.Response.Headers[HeaderNames.CacheControl] = "private, no-store";
        if (file.Inline)
        {
            http.Response.Headers[HeaderNames.ContentDisposition] = new ContentDispositionHeaderValue("inline") { FileNameStar = file.FileName }.ToString();
            return TypedResults.Stream(file.Content, file.ContentType);
        }

        return TypedResults.Stream(file.Content, file.ContentType, fileDownloadName: file.FileName);
    }
}

internal sealed class UpdateMaterialRequestValidator : AbstractValidator<UpdateMaterialRequest>
{
    public UpdateMaterialRequestValidator()
    {
        RuleFor(x => x.Title).Must(t => t is not null && t.Trim().Length is >= 1 and <= Material.TitleMaxLength)
            .WithMessage($"Enter a title (up to {Material.TitleMaxLength} characters).");
        RuleFor(x => x.Description).MaximumLength(Material.DescriptionMaxLength);
        RuleFor(x => x.Version).NotNull();
    }
}

internal sealed class AssignMaterialRequestValidator : AbstractValidator<AssignMaterialRequest>
{
    public AssignMaterialRequestValidator() =>
        RuleFor(x => x).Must(x => (x.StudentId is null) != (x.GroupId is null)).OverridePropertyName("studentId").WithMessage("Choose a student or a group.");
}
