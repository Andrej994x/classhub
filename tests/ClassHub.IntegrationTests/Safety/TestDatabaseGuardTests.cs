using ClassHub.IntegrationTests.Infrastructure;

namespace ClassHub.IntegrationTests.Safety;

/// <summary>Guard checks that need no Docker: the guards must refuse every non-container database.</summary>
public sealed class TestDatabaseGuardTests
{
    private const string Container = "localhost,49153";
    private const string Valid =
        "Server=localhost,49153;Database=ClassHubTest_0123456789abcdef;User Id=sa;Password=x;TrustServerCertificate=True";

    [Fact]
    public void Accepts_the_container_test_database()
    {
        Should.NotThrow(() => TestDatabaseGuard.AssertIsolated(Valid, Container));
    }

    [Theory]
    [InlineData("Server=.\\SQLEXPRESS;Database=ClassHubTest_x;Integrated Security=True")]
    [InlineData("Server=.;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=(local);Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=(localdb)\\MSSQLLocalDB;Database=ClassHubTest_x;Integrated Security=True")]
    [InlineData("Server=np:\\\\.\\pipe\\MSSQL$SQLEXPRESS\\sql\\query;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=lpc:.;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=MACHINE\\SQLEXPRESS;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=localhost,1433;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=db.example.com,49153;Database=ClassHubTest_x;User Id=sa;Password=x")]
    [InlineData("Server=localhost,49153;Database=ClassHubTest_x;Integrated Security=True")]
    [InlineData("Server=localhost,49153;Database=ClassHubTest_x;User Id=sa;Password=x;AttachDbFilename=C:\\x.mdf")]
    [InlineData("Server=localhost,49153;Database=ClassHubDb;User Id=sa;Password=x")]
    [InlineData("Server=localhost,49153;Database=master;User Id=sa;Password=x")]
    [InlineData("Server=localhost,49153;Database=ClassHubTest_;User Id=sa;Password=x")]
    [InlineData("Server=localhost,49153;Database=SomethingElse;User Id=sa;Password=x")]
    [InlineData("")]
    [InlineData("not a connection string")]
    public void Refuses_anything_but_the_container_test_database(string connectionString)
    {
        Should.Throw<InvalidOperationException>(() => TestDatabaseGuard.AssertIsolated(connectionString, Container));
    }

    [Fact]
    public void Refuses_when_no_container_endpoint_is_known()
    {
        Should.Throw<InvalidOperationException>(() => TestDatabaseGuard.AssertIsolated(Valid, string.Empty));
    }

    [Theory]
    [InlineData("npipe://./pipe/docker_engine", true)]
    [InlineData("unix:///var/run/docker.sock", true)]
    [InlineData("tcp://127.0.0.1:2375", true)]
    [InlineData("tcp://localhost:2375", true)]
    [InlineData("tcp://10.0.0.5:2375", false)]
    [InlineData("tcp://docker.example.com:2376", false)]
    [InlineData("ssh://user@host", false)]
    public void Docker_engine_must_be_local(string endpoint, bool allowed)
    {
        var uri = new Uri(endpoint);
        if (allowed)
        {
            Should.NotThrow(() => DockerEndpointGuard.AssertLocalEngine(uri));
        }
        else
        {
            Should.Throw<InvalidOperationException>(() => DockerEndpointGuard.AssertLocalEngine(uri));
        }
    }

    [Fact]
    public void Docker_engine_is_required()
    {
        Should.Throw<InvalidOperationException>(() => DockerEndpointGuard.AssertLocalEngine(null));
    }

    [Theory]
    [InlineData("localhost", 1433)]
    [InlineData("10.0.0.5", 49153)]
    [InlineData("db.example.com", 49153)]
    public void Container_endpoint_must_be_loopback_and_not_1433(string host, int port)
    {
        Should.Throw<InvalidOperationException>(() => DockerEndpointGuard.AssertLocalContainerEndpoint(host, port));
    }
}
