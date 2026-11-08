using ClassHub.Core.Infrastructure.Web;
using ClassHub.Core.Modules.Groups.Application;
using ClassHub.Core.Modules.Groups.Contracts;
using ClassHub.Core.Modules.Groups.Domain;
using ClassHub.Core.Modules.Groups.Persistence;
using ClassHub.Core.Modules.Workspaces;
using ClassHub.Core.SharedKernel.Security;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ClassHub.Core.Modules.Groups;

/// <summary>Groups (M06, R2-P2): the teacher's teaching groups and their members.</summary>
public sealed class GroupsModule : IModule
{
    public void AddServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<GroupQueries>();
        services.AddScoped<GroupService>();
        services.AddScoped<IGroupLookup, GroupLookup>();
    }

    public void MapEndpoints(IEndpointRouteBuilder endpoints)
    {
        var groups = endpoints.MapGroup($"{WorkspacesModule.WorkspaceRoute}/groups").WithTags("Groups").RequireAntiforgeryHeader();
        var read = WorkspacePolicies.For(WorkspacePermissions.GroupsRead);
        var manage = WorkspacePolicies.For(WorkspacePermissions.GroupsManage);

        groups.MapGet("", async (bool? archived, Guid? studentId, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.ListAsync(archived ?? false, studentId, ct)))
            .RequireAuthorization(read)
            .WithName("ListGroups")
            .WithSummary("Active (or archived) groups by name; optionally only those a student is a member of.");

        groups.MapGet("/{groupId:guid}", async (Guid groupId, GroupService service, CancellationToken ct) => TypedResults.Ok(await service.GetAsync(groupId, ct)))
            .RequireAuthorization(read)
            .WithName("GetGroup");

        groups.MapPost("", async (SaveGroupRequest request, GroupService service, CancellationToken ct) =>
                TypedResults.Created((string?)null, await service.CreateAsync(request, ct)))
            .WithValidation<SaveGroupRequest>()
            .RequireAuthorization(manage)
            .WithName("CreateGroup");

        groups.MapPut("/{groupId:guid}", async (Guid groupId, SaveGroupRequest request, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.UpdateAsync(groupId, request, ct)))
            .WithValidation<SaveGroupRequest>()
            .RequireAuthorization(manage)
            .WithName("UpdateGroup");

        groups.MapPost("/{groupId:guid}/archive", async (Guid groupId, GroupVersionRequest request, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(groupId, request, archived: true, ct)))
            .RequireAuthorization(manage)
            .WithName("ArchiveGroup");

        groups.MapPost("/{groupId:guid}/restore", async (Guid groupId, GroupVersionRequest request, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.SetArchivedAsync(groupId, request, archived: false, ct)))
            .RequireAuthorization(manage)
            .WithName("RestoreGroup");

        groups.MapPost("/{groupId:guid}/members", async (Guid groupId, AddGroupMemberRequest request, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.AddMemberAsync(groupId, request, ct)))
            .WithValidation<AddGroupMemberRequest>()
            .RequireAuthorization(manage)
            .WithName("AddGroupMember");

        groups.MapDelete("/{groupId:guid}/members/{studentId:guid}", async (Guid groupId, Guid studentId, GroupService service, CancellationToken ct) =>
                TypedResults.Ok(await service.RemoveMemberAsync(groupId, studentId, ct)))
            .RequireAuthorization(manage)
            .WithName("RemoveGroupMember")
            .WithSummary("End the membership; the record stays for the lesson history.");
    }
}

internal sealed class SaveGroupRequestValidator : AbstractValidator<SaveGroupRequest>
{
    public SaveGroupRequestValidator()
    {
        RuleFor(x => x.Name).Must(n => n is not null && n.Trim().Length is >= 1 and <= Group.NameMaxLength)
            .WithMessage($"Enter a name (up to {Group.NameMaxLength} characters).");
        RuleFor(x => x.Description).MaximumLength(Group.DescriptionMaxLength);
    }
}

internal sealed class AddGroupMemberRequestValidator : AbstractValidator<AddGroupMemberRequest>
{
    public AddGroupMemberRequestValidator() => RuleFor(x => x.StudentId).NotNull();
}
