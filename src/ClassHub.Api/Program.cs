using ClassHub.Api;
using ClassHub.Api.Platform;
using ClassHub.Core.Infrastructure.Email;
using ClassHub.Core.Infrastructure.Persistence;
using ClassHub.Core.Modules;
using ClassHub.Core.SharedKernel.Errors;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Scalar.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .Enrich.With<SensitiveDataRedactionEnricher>()
    .WriteTo.Console());

// Request size limit for JSON APIs (uploads get their own limits per endpoint later).
builder.WebHost.ConfigureKestrel(kestrel => kestrel.Limits.MaxRequestBodySize = 1 * 1024 * 1024);

builder.Services.AddProblemDetails(options => options.CustomizeProblemDetails = context =>
{
    var status = context.ProblemDetails.Status ?? context.HttpContext.Response.StatusCode;
    context.ProblemDetails.Extensions.TryAdd("code", ErrorCodes.ForStatus(status));
});
builder.Services.AddExceptionHandler<ClassHubExceptionHandler>();

builder.Services.AddClassHubSecurity(builder.Configuration, builder.Environment);
builder.Services.AddClassHubRateLimiting(builder.Configuration);
builder.Services.AddClassHubPersistence(builder.Configuration);
builder.Services.AddClassHubEmail(builder.Configuration, builder.Environment);
builder.Services.AddClassHubModules(builder.Configuration);
builder.Services.AddOpenApi("v1", options => options.AddOperationTransformer<RouteParameterOperationTransformer>());
// Server-rendered pages: the site preview now (R1-P3), the public teacher sites with R1-P4.
builder.Services.AddRazorPages();

var app = builder.Build();

// Local administrative commands (never exposed over HTTP), e.g. `-- grant-platform-owner <email>`.
if (await PlatformCommands.TryRunAsync(app, args))
{
    return;
}

// E2E only: migrate the disposable Playwright database (guarded; never ClassHubDb).
await E2EDatabase.MigrateIfE2EAsync(app);

if (app.Services.GetRequiredService<ForwardedHeadersSettings>().Enabled)
{
    app.UseForwardedHeaders();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("Testing") && !app.Environment.IsEnvironment("E2E"))
{
    app.UseHsts();
}

app.UseMiddleware<SecurityHeadersMiddleware>();
app.UseSerilogRequestLogging();
// The built SPA (wwwroot/dist) is static and public; the data it shows comes from protected APIs.
app.UseClassHubStaticFiles();
app.UseRouting();
app.UseAuthentication();
// Before authorization: requests that authorization rejects (401/403, unknown routes) still count
// against the client's limits. After routing, so endpoint-specific policies apply.
app.UseRateLimiter();
app.UseAuthorization();

// Liveness: the process answers. No dependencies, no details.
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .AllowAnonymous()
    .WithName("Health")
    .ExcludeFromDescription();
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// Readiness: the database is reachable. The response is only "Healthy"/"Unhealthy" (no details).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains(PersistenceServiceCollectionExtensions.ReadinessTag),
    })
    .AllowAnonymous();

app.MapClassHubModules();
app.MapRazorPages();

// The OpenAPI document is the contract for the generated TypeScript types (web/src/api/schema.d.ts).
if (app.Environment.IsDevelopment() || app.Environment.IsEnvironment("Testing"))
{
    app.MapOpenApi().AllowAnonymous();
}

if (app.Environment.IsDevelopment())
{
    app.MapScalarApiReference().AllowAnonymous();
}

// Last: the SPA's own areas fall back to index.html (never /api or health checks).
app.MapClassHubSpa();

await app.RunAsync();

/// <summary>Entry point; partial so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
