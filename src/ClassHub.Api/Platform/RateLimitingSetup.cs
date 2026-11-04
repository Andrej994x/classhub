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
    /// Fixed-window limits per client IP for the named policies; rejected requests get a 429
    /// ProblemDetails (code <see cref="ErrorCodes.RateLimited"/>) with Retry-After. In-memory only (no Redis).
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
            var partitionKey = http.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            return RateLimitPartition.GetFixedWindowLimiter(partitionKey, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permits,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true,
            });
        });
    }
}
