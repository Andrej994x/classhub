namespace ClassHub.Api;

/// <summary>Response of the liveness endpoint <c>GET /health</c>.</summary>
public sealed record HealthResponse(string Status);
