using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace LifeOs.Tests;

/// <summary>
/// <c>web/src/api/openapi.json</c> is the API's OpenAPI document, and the web's
/// <c>schema.d.ts</c> is generated from it (code review CR9). Checked here, so CI fails when an
/// endpoint or a read model changes and the file doesn't. Regenerate both with
/// <c>./scripts/generate-web-client.ps1</c>.
/// </summary>
public sealed class OpenApiDocumentTests(PostgresFixture postgres)
{
    /// <summary>Set to 1 to write the document instead of checking it.</summary>
    public const string WriteVariable = "LIFEOS_WRITE_OPENAPI";

    private static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_web_client_document_is_current()
    {
        await using var database = await DomainTestDatabase.CreateAsync(postgres, Ct);
        await using var host = await ApiTestHost.StartAsync(database.ConnectionString, Ct, DomainTestDatabase.ApiZone);
        var document = JsonNode.Parse(await host.Client.GetStringAsync("/openapi/v1.json", Ct))!;
        var actual = document.ToJsonString(Indented).Replace("\r\n", "\n") + "\n";
        var path = Path.Combine(RepoRoot.Path, "web", "src", "api", "openapi.json");

        if (Environment.GetEnvironmentVariable(WriteVariable) == "1")
        {
            await File.WriteAllTextAsync(path, actual, new UTF8Encoding(false), Ct);
            return;
        }

        var committed = File.Exists(path) ? (await File.ReadAllTextAsync(path, Ct)).Replace("\r\n", "\n") : "";
        Assert.True(
            committed == actual,
            "web/src/api/openapi.json is out of date. Run ./scripts/generate-web-client.ps1 and commit the result.");
    }
}
