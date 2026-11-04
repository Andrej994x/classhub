using ClassHub.Api;
using ClassHub.Core.Infrastructure.Persistence;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

builder.Services.AddClassHubPersistence(builder.Configuration);

var app = builder.Build();

// Liveness: the process answers. No dependencies, no details.
app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .AllowAnonymous()
    .WithName("Health");
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false })
    .AllowAnonymous();

// Readiness: the database is reachable. The response is only "Healthy"/"Unhealthy" (no details).
app.MapHealthChecks("/health/ready", new HealthCheckOptions
    {
        Predicate = check => check.Tags.Contains(PersistenceServiceCollectionExtensions.ReadinessTag),
    })
    .AllowAnonymous();

await app.RunAsync();

/// <summary>Entry point; partial so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
