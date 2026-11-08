using System.Security.Claims;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using ClassHub.Core.SharedKernel.Tenancy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Policy;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ClassHub.Core.Infrastructure.Tenancy;

/// <summary>Looks up a user's active role in a workspace (implemented by the Workspaces module).</summary>
public interface IWorkspaceMembershipLookup
{
    Task<WorkspaceRole?> GetActiveRoleAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>Requires an active membership in the route's workspace with the given permission.</summary>
public sealed class WorkspacePermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Verifies membership in the <c>{workspaceId}</c> route value and the role's permission, then binds the
/// request to that workspace (<see cref="TenantContext"/>). Non-members fail with <see cref="NotMemberReason"/>,
/// which becomes 404 so other tenants' workspaces stay invisible.
/// </summary>
public sealed class WorkspacePermissionHandler(IWorkspaceMembershipLookup memberships, TenantContext tenant)
    : AuthorizationHandler<WorkspacePermissionRequirement>
{
    public const string RouteValueName = "workspaceId";
    public const string NotMemberReason = "workspace.not-member";
    public const string MissingPermissionReason = "workspace.missing-permission";

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, WorkspacePermissionRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (context.Resource is not HttpContext http ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return; // unauthenticated: the challenge (401) is handled by the authentication layer
        }

        if (!http.Request.RouteValues.TryGetValue(RouteValueName, out var raw) ||
            !Guid.TryParse(raw?.ToString(), out var workspaceId))
        {
            context.Fail(new AuthorizationFailureReason(this, NotMemberReason));
            return;
        }

        var role = await memberships.GetActiveRoleAsync(workspaceId, userId, http.RequestAborted);
        if (role is null)
        {
            context.Fail(new AuthorizationFailureReason(this, NotMemberReason));
            return;
        }

        if (!WorkspaceRolePermissions.Has(role.Value, requirement.Permission))
        {
            context.Fail(new AuthorizationFailureReason(this, MissingPermissionReason));
            return;
        }

        tenant.Set(workspaceId, userId, role.Value);
        context.Succeed(requirement);
    }
}

/// <summary>Finds the user's active (not archived) student record in a workspace (implemented by the Students module).</summary>
public interface IStudentSelfLookup
{
    Task<Guid?> FindActiveStudentAsync(Guid workspaceId, Guid userId, CancellationToken cancellationToken);
}

/// <summary>The <c>student:self</c> requirement: an active student record of the user in the route's workspace.</summary>
public sealed class StudentSelfRequirement : IAuthorizationRequirement;

/// <summary>
/// Verifies the user's own student record in the <c>{workspaceId}</c> route value and binds the request to it. Users
/// without one fail with <see cref="WorkspacePermissionHandler.NotMemberReason"/> (404).
/// </summary>
public sealed class StudentSelfHandler(IStudentSelfLookup students, TenantContext tenant) : AuthorizationHandler<StudentSelfRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, StudentSelfRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.Resource is not HttpContext http ||
            !Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return;
        }

        if (!http.Request.RouteValues.TryGetValue(WorkspacePermissionHandler.RouteValueName, out var raw) ||
            !Guid.TryParse(raw?.ToString(), out var workspaceId) ||
            await students.FindActiveStudentAsync(workspaceId, userId, http.RequestAborted) is not { } studentId)
        {
            context.Fail(new AuthorizationFailureReason(this, WorkspacePermissionHandler.NotMemberReason));
            return;
        }

        tenant.SetStudent(workspaceId, userId, studentId);
        context.Succeed(requirement);
    }
}

/// <summary>Builds <c>ws:&lt;permission&gt;</c> policies on demand; everything else uses the defaults.</summary>
public sealed class WorkspacePolicyProvider(IOptions<AuthorizationOptions> options) : DefaultAuthorizationPolicyProvider(options)
{
    public override async Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        ArgumentNullException.ThrowIfNull(policyName);
        if (policyName.StartsWith(WorkspacePolicies.Prefix, StringComparison.Ordinal))
        {
            var permission = policyName[WorkspacePolicies.Prefix.Length..];
            if (!WorkspacePermissions.All.Contains(permission))
            {
                throw new InvalidOperationException($"Unknown workspace permission '{permission}'.");
            }

            return new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new WorkspacePermissionRequirement(permission))
                .Build();
        }

        return await base.GetPolicyAsync(policyName);
    }
}

/// <summary>
/// Turns "not a member of this workspace" into 404 (existence is never revealed), and a platform staff session without
/// the authenticator into 403 <c>MFA_REQUIRED</c>.
/// </summary>
public sealed class WorkspaceAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler
{
    private readonly AuthorizationMiddlewareResultHandler _default = new();

    public async Task HandleAsync(RequestDelegate next, HttpContext context, AuthorizationPolicy policy, PolicyAuthorizationResult authorizeResult)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(authorizeResult);

        if (authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Message == WorkspacePermissionHandler.NotMemberReason) == true)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Title = "The resource was not found.",
                    Extensions = { ["code"] = ErrorCodes.NotFound },
                },
            });
            return;
        }

        if (authorizeResult.Forbidden &&
            authorizeResult.AuthorizationFailure?.FailureReasons.Any(r => r.Message == ClassHub.Core.Infrastructure.Web.PlatformStaffHandler.MfaRequiredReason) == true)
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            var problems = context.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problems.TryWriteAsync(new ProblemDetailsContext
            {
                HttpContext = context,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status403Forbidden,
                    Title = "Sign in with your authenticator app first.",
                    Extensions = { ["code"] = ErrorCodes.MfaRequired },
                },
            });
            return;
        }

        await _default.HandleAsync(next, context, policy, authorizeResult);
    }
}

public static class TenancyServiceCollectionExtensions
{
    public static IServiceCollection AddClassHubTenancy(this IServiceCollection services)
    {
        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<TenantContext>());
        services.AddScoped<IAuthorizationHandler, WorkspacePermissionHandler>();
        services.AddScoped<IAuthorizationHandler, StudentSelfHandler>();
        services.AddSingleton<IAuthorizationPolicyProvider, WorkspacePolicyProvider>();
        services.AddSingleton<IAuthorizationMiddlewareResultHandler, WorkspaceAuthorizationResultHandler>();
        return services;
    }
}
