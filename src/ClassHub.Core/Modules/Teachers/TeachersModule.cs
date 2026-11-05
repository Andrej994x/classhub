using ClassHub.Core.Infrastructure.Files;
using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Teachers.Application;
using ClassHub.Core.Modules.Teachers.Contracts;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Net.Http.Headers;

namespace ClassHub.Core.Modules.Teachers;

/// <summary>Teacher profile, profile photo and the derived onboarding checklist (R1-P1).</summary>
public sealed class TeachersModule : IModule
{
    /// <summary>Upper bound for the photo request (the image itself is limited to 5 MB).</summary>
    public const long PhotoRequestLimitBytes = 6 * 1024 * 1024;

    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<TeacherProfileService>();
        services.AddScoped<ITeacherSummary>(sp => sp.GetRequiredService<TeacherProfileService>());
        services.AddClassHubFiles(configuration);
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var workspace = endpoints.MapGroup(WorkspacesModule.WorkspaceRoute).WithTags("Teacher profile").RequireAntiforgeryHeader();

        workspace.MapGet("/profile", async (TeacherProfileService profiles, CancellationToken ct) => TypedResults.Ok(await profiles.GetAsync(ct)))
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileRead))
            .WithName("GetTeacherProfile");

        workspace.MapPut("/profile", async (UpdateTeacherProfileRequest request, TeacherProfileService profiles, CancellationToken ct) =>
                TypedResults.Ok(await profiles.UpdateAsync(request, ct)))
            .WithValidation<UpdateTeacherProfileRequest>()
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileManage))
            .WithName("UpdateTeacherProfile");

        // multipart/form-data with one part named "file". Read manually (not IFormFile binding): the API's
        // antiforgery header filter protects it like every other unsafe request.
        workspace.MapPut("/profile/photo", async Task<Ok<TeacherProfileResponse>> (HttpContext http, TeacherProfileService profiles, CancellationToken ct) =>
            {
                var sizeLimit = http.Features.Get<IHttpMaxRequestBodySizeFeature>();
                if (sizeLimit is { IsReadOnly: false })
                {
                    sizeLimit.MaxRequestBodySize = PhotoRequestLimitBytes;
                }

                if (!http.Request.HasFormContentType)
                {
                    throw new BadRequestException("Send the photo as multipart/form-data.", ErrorCodes.UnsupportedMediaType);
                }

                IFormCollection form;
                try
                {
                    form = await http.Request.ReadFormAsync(ct);
                }
                catch (InvalidDataException)
                {
                    // Malformed multipart (no boundary, broken parts, too many parts): the client's fault, not ours.
                    throw new BadRequestException("The upload is not valid multipart/form-data.", ErrorCodes.BadRequest);
                }
                var file = form.Files.GetFile("file") ?? throw new BadRequestException("The form has no file part named 'file'.", ErrorCodes.ImageInvalid);
                if (file.Length > ImageRules.ProfilePhoto.MaxBytes)
                {
                    throw new BadRequestException("The image must be at most 5 MB.", ErrorCodes.ImageInvalid);
                }

                using var buffer = new MemoryStream((int)file.Length);
                await file.CopyToAsync(buffer, ct);
                return TypedResults.Ok(await profiles.SetPhotoAsync(buffer.ToArray(), ct));
            })
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileManage))
            .Accepts<IFormFile>("multipart/form-data")
            .WithName("SetTeacherProfilePhoto")
            .WithSummary("Upload the profile photo (JPEG, PNG or WebP, at most 5 MB, at least 128 × 128). Stored re-encoded as WebP without metadata.");

        workspace.MapDelete("/profile/photo", async (TeacherProfileService profiles, CancellationToken ct) =>
            {
                await profiles.RemovePhotoAsync(ct);
                return TypedResults.NoContent();
            })
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileManage))
            .WithName("RemoveTeacherProfilePhoto");

        // Private for now: only members of the workspace see the photo. It becomes public with the published
        // site (R1-P4) through a separate public endpoint.
        workspace.MapGet("/profile/photo", async Task<Results<FileStreamHttpResult, NotFound>> (HttpContext http, TeacherProfileService profiles, CancellationToken ct) =>
            {
                var photo = await profiles.OpenPhotoAsync(ct);
                if (photo is not { } p)
                {
                    return TypedResults.NotFound();
                }

                http.Response.Headers[HeaderNames.CacheControl] = "private, max-age=300";
                return TypedResults.Stream(p.Content, p.ContentType, entityTag: EntityTagHeaderValue.Parse(p.ETag));
            })
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileRead))
            .WithName("GetTeacherProfilePhoto");

        workspace.MapGet("/onboarding", async (TeacherProfileService profiles, CancellationToken ct) => TypedResults.Ok(await profiles.GetOnboardingAsync(ct)))
            .RequireAuthorization(WorkspacePolicies.For(WorkspacePermissions.ProfileRead))
            .WithName("GetOnboarding")
            .WithSummary("Onboarding checklist derived from the workspace's data (never ticked manually).");
    }
}
