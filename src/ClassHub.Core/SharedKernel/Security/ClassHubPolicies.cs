namespace ClassHub.Core.SharedKernel.Security;

/// <summary>
/// Named authorization policies. Every endpoint declares either <c>AllowAnonymous</c> or one of these
/// policies explicitly (enforced by the endpoint-metadata test); the fallback policy is a safety net only.
/// </summary>
public static class ClassHubPolicies
{
    /// <summary>Any signed-in, active user.</summary>
    public const string Authenticated = "Authenticated";

    /// <summary>Platform Owner or Platform Admin, signed in with the authenticator (<c>amr=mfa</c>).</summary>
    public const string PlatformStaff = "platform:staff";

    /// <summary>Platform Owner, signed in with the authenticator (<c>amr=mfa</c>).</summary>
    public const string PlatformOwner = "platform:owner";
}

/// <summary>Authentication-method claims (RFC 8176 <c>amr</c>) that ASP.NET Core Identity issues at sign-in.</summary>
public static class AuthenticationMethods
{
    public const string ClaimType = "amr";

    /// <summary>Password only.</summary>
    public const string Password = "pwd";

    /// <summary>Password plus the authenticator app or a recovery code.</summary>
    public const string MultiFactor = "mfa";
}

/// <summary>Named rate-limiting policies (limits are configured under <c>RateLimiting</c>).</summary>
public static class RateLimitPolicies
{
    /// <summary>Login, registration, password reset and similar authentication endpoints.</summary>
    public const string Auth = "auth";

    /// <summary>Anonymous public form submissions (contact, test, booking, abuse report).</summary>
    public const string PublicForm = "public-form";

    /// <summary>Anonymous public reads (public sites, public tests).</summary>
    public const string PublicRead = "public-read";
}
