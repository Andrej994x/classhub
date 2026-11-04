namespace ClassHub.Core.SharedKernel.Security;

/// <summary>
/// Named authorization policies. Every endpoint declares either <c>AllowAnonymous</c> or one of these
/// policies explicitly (enforced by the endpoint-metadata test); the fallback policy is a safety net only.
/// </summary>
public static class ClassHubPolicies
{
    /// <summary>Any signed-in, active user.</summary>
    public const string Authenticated = "Authenticated";
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
