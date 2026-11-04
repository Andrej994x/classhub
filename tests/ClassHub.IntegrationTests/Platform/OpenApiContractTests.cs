using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClassHub.IntegrationTests.Platform;

/// <summary>
/// The committed OpenAPI document (<c>web/src/api/openapi.json</c>) must equal what the API produces;
/// the frontend generates its TypeScript types from it (CI checks that the generated types are current).
/// To update after an intended API change: <c>$env:CLASSHUB_UPDATE_OPENAPI='1'; dotnet test --project tests/ClassHub.IntegrationTests</c>.
/// </summary>
public sealed class OpenApiContractTests
{
    private const string UpdateVariable = "CLASSHUB_UPDATE_OPENAPI";

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    [Fact]
    public async Task Committed_openapi_document_matches_the_api()
    {
        using var factory = new PlatformApiFactory(includeProbeModule: false);
        using var client = factory.CreateClient();

        var json = await client.GetStringAsync(new Uri("/openapi/v1.json", UriKind.Relative), TestContext.Current.CancellationToken);
        var actual = Normalize(json);

        var path = Path.Combine(RepositoryRoot(), "web", "src", "api", "openapi.json");
        if (Environment.GetEnvironmentVariable(UpdateVariable) == "1")
        {
            await File.WriteAllTextAsync(path, actual, TestContext.Current.CancellationToken);
            return;
        }

        File.Exists(path).ShouldBeTrue($"{path} is missing. Run the tests with {UpdateVariable}=1 to create it.");
        var committed = Normalize(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken));
        committed.ShouldBe(actual,
            $"web/src/api/openapi.json is out of date. Run the tests with {UpdateVariable}=1, then `npm run api:types` in web/, and commit both files.");
    }

    private static string Normalize(string json)
    {
        var node = JsonNode.Parse(json) ?? throw new InvalidOperationException("Empty OpenAPI document.");
        // The server URL depends on the test host; it is not part of the contract.
        if (node is JsonObject root)
        {
            root.Remove("servers");
        }

        return node.ToJsonString(WriteOptions).ReplaceLineEndings("\n") + "\n";
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ClassHub.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Repository root (ClassHub.slnx) not found.");
    }
}
