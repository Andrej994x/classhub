using System.Net;

namespace ClassHub.IntegrationTests.Infrastructure;

/// <summary>
/// Before any container starts: Testcontainers must talk to a Docker engine on this machine, and the
/// SQL Server endpoint it returns must be a loopback host on a random (never the default 1433) port.
/// </summary>
public static class DockerEndpointGuard
{
    /// <summary>The default SQL Server port. A test container is never reached through it.</summary>
    public const int DefaultSqlServerPort = 1433;

    public static void AssertLocalEngine(Uri? endpoint)
    {
        if (endpoint is null)
        {
            throw new InvalidOperationException(
                "Docker is required for the integration tests, but no Docker endpoint was found. Start Docker " +
                "Desktop (Linux containers). The tests never fall back to another SQL Server.");
        }

        var isLocal = endpoint.Scheme switch
        {
            "npipe" or "unix" => true,
            "tcp" or "http" or "https" => IsLoopback(endpoint.Host),
            _ => false,
        };

        if (!isLocal)
        {
            throw new InvalidOperationException(
                $"Refusing to run integration tests against the non-local Docker endpoint '{endpoint}'.");
        }
    }

    public static void AssertLocalContainerEndpoint(string host, int mappedPort)
    {
        if (!IsLoopback(host))
        {
            throw new InvalidOperationException(
                $"Refusing to use test container host '{host}': only a loopback address on this machine is allowed.");
        }

        if (mappedPort == DefaultSqlServerPort)
        {
            throw new InvalidOperationException(
                $"Refusing to use port {DefaultSqlServerPort}: it could be a locally installed SQL Server.");
        }
    }

    private static bool IsLoopback(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address));
}
