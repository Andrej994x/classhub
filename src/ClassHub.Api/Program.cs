using ClassHub.Api;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSerilog((services, logger) => logger
    .ReadFrom.Configuration(builder.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console());

var app = builder.Build();

app.MapGet("/health", () => TypedResults.Ok(new HealthResponse("ok")))
    .AllowAnonymous()
    .WithName("Health");

await app.RunAsync();

/// <summary>Entry point; partial so integration tests can use <c>WebApplicationFactory&lt;Program&gt;</c>.</summary>
public partial class Program;
