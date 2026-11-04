using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Threading.RateLimiting;
using ClassHub.Core.SharedKernel.Errors;
using ClassHub.Core.SharedKernel.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ClassHub.Api.Platform;

/// <summary>Configurable limits for the named rate-limiting policies (section <c>RateLimiting</c>).</summary>
public sealed class RateLimitingOptions
{
    public const string SectionName = "RateLimiting";

    /// <summary>Default limit for every <c>/api</c> request per client IP (applied before any named policy).</summary>
    [Range(1, 1_000_000)] public int DefaultPermitLimit { get; set; } = 300;
    [Range(1, 86_400)] public int DefaultWindowSeconds { get; set; } = 60;

    [Range(1, 100_000)] public int AuthPermitLimit { get; set; } = 10;
    [Range(1, 86_400)] public int AuthWindowSeconds { get; set; } = 60;
    [Range(1, 100_000)] public int PublicFormPermitLimit { get; set; } = 5;
    [Range(1, 86_400)] public int PublicFormWindowSeconds { get; set; } = 60;
    [Range(1, 100_000)] public int PublicReadPermitLimit { get; set; } = 120;
    [Range(1, 86_400)] public int PublicReadWindowSeconds { get; set; } = 60;
}

internal static class RateLimitingSetup
{
    /// <summary>
    /// A default fixed-window limit per client IP for every <c>/api</c> request (global limiter), plus
    /// stricter named policies (<see cref="RateLimitPolicies"/>) that endpoints opt into on top of it.
    /// Health checks and non-API paths are not limited here. Rejected requests get a 429 ProblemDetails
    /// (code <see cref="ErrorCodes.RateLimited"/>) with Retry-After. In-memory only (no Redis).
    /// </summary>
    public static IServiceCollection AddClassHubRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<RateLimitingOptions>()
            .Bind(configuration.GetSection(RateLimitingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(limiter =>
        {
            limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            limiter.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(http =>
            {
                if (!http.Request.Path.StartsWithSegments("/api"))
                {
                    return RateLimitPartition.GetNoLimiter("unlimited");
                }

                var options = http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value;
                return RateLimitPartition.GetFixedWindowLimiter(ClientPartitionKey(http), _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = options.DefaultPermitLimit,
                    Window = TimeSpan.FromSeconds(options.DefaultWindowSeconds),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                });
            });

            AddFixedWindow(limiter, RateLimitPolicies.Auth, o => (o.AuthPermitLimit, o.AuthWindowSeconds));
            AddFixedWindow(limiter, RateLimitPolicies.PublicForm, o => (o.PublicFormPermitLimit, o.PublicFormWindowSeconds));
            AddFixedWindow(limiter, RateLimitPolicies.PublicRead, o => (o.PublicReadPermitLimit, o.PublicReadWindowSeconds));

            limiter.OnRejected = async (context, cancellationToken) =>
            {
                var http = context.HttpContext;
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    http.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                var problemDetails = http.RequestServices.GetRequiredService<IProblemDetailsService>();
                await problemDetails.TryWriteAsync(new ProblemDetailsContext
                {
                    HttpContext = http,
                    ProblemDetails = new ProblemDetails
                    {
                        Status = StatusCodes.Status429TooManyRequests,
                        Title = "Too many requests. Please wait and try again.",
                        Extensions = { ["code"] = ErrorCodes.RateLimited },
                    },
                });
            };
        });

        return services;
    }

    private static void AddFixedWindow(
        RateLimiterOptions limiter, string policyName, Func<RateLimitingOptions, (int Permits, int WindowSeconds)> select)
    {
        limiter.AddPolicy(policyName, http =>
        {
            var (permits, windowSeconds) = select(http.RequestServices.GetRequiredService<IOptions<RateLimitingOptions>>().Value);
            return RateLimitPartition.GetFixedWindowLimiter(ClientPartitionKey(http), _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }

    /// <summary>The client IP (after trusted forwarded headers, if enabled).</summary>
    private static string ClientPartitionKey(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
