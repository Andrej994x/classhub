using System.Security.Claims;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;

namespace ClassHub.Core.Infrastructure.Web;

/// <summary>
/// Platform administration (R1-P9): a platform role (any of <see cref="Roles"/>) and a session signed in with the
/// authenticator. A staff member without such a session fails with <see cref="MfaRequiredReason"/> (403 <c>MFA_REQUIRED</c>,
/// so the web app can offer the enrollment or a new sign-in). Platform roles never grant workspace access.
/// </summary>
public sealed class PlatformStaffRequirement(params string[] roles) : IAuthorizationRequirement
{
    public IReadOnlyList<string> Roles { get; } = roles;
}

public sealed class PlatformStaffHandler : AuthorizationHandler<PlatformStaffRequirement>
{
    public const string MfaRequiredReason = "platform.mfa-required";

    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, PlatformStaffRequirement requirement)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requirement);

        if (!requirement.Roles.Any(context.User.IsInRole))
        {
            return Task.CompletedTask; // plain 403
        }

        if (!context.User.HasClaim(AuthenticationMethods.ClaimType, AuthenticationMethods.MultiFactor))
        {
            context.Fail(new AuthorizationFailureReason(this, MfaRequiredReason));
            return Task.CompletedTask;
        }

        context.Succeed(requirement);
        return Task.CompletedTask;
    }
}

public static class PlatformAuthorization
{
    /// <summary>The <c>platform:staff</c> and <c>platform:owner</c> policies.</summary>
    public static AuthorizationBuilder AddPlatformPolicies(this AuthorizationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddPolicy(ClassHubPolicies.PlatformStaff, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new PlatformStaffRequirement(PlatformRoles.Owner, PlatformRoles.Admin)))
            .AddPolicy(ClassHubPolicies.PlatformOwner, policy => policy.RequireAuthenticatedUser()
                .AddRequirements(new PlatformStaffRequirement(PlatformRoles.Owner)));
    }

    public static bool IsPlatformStaff(ClaimsPrincipal? principal) =>
        principal is not null && (principal.IsInRole(PlatformRoles.Owner) || principal.IsInRole(PlatformRoles.Admin));

    /// <summary>
    /// Shorter sessions for platform staff: never a persistent cookie, and it ends after <paramref name="lifetime"/>
    /// without activity (sliding, like every session). Applied whenever the session cookie is issued.
    /// </summary>
    public static void LimitStaffSession(CookieSigningInContext context, TimeSpan lifetime)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (!IsPlatformStaff(context.Principal))
        {
            return;
        }

        var issued = context.Properties.IssuedUtc ?? DateTimeOffset.UtcNow;
        var limit = issued + lifetime;
        context.Properties.IsPersistent = false;
        if (context.Properties.ExpiresUtc is null || context.Properties.ExpiresUtc > limit)
        {
            context.Properties.ExpiresUtc = limit;
        }
    }
}
