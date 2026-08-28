using System.Net;
using System.Text.Json;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Documentation;

[Collection("cash-flow")]
public sealed class OpenApiDocumentationTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _entriesFactory = EntriesApiFactory.For(containers);
    private readonly ConsolidationApiFactory _consolidationFactory =
        ConsolidationApiFactory.For(containers);

    [DockerRequiredFact]
    public async Task EntriesApi_SwaggerUi_Returns200AndHtml()
    {
        var client = _entriesFactory.CreateClient();

        var response = await client.GetAsync("/swagger");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Swagger UI");
    }

    [DockerRequiredFact]
    public async Task EntriesApi_ScalarUi_Returns200AndHtml()
    {
        var client = _entriesFactory.CreateClient();

        var response = await client.GetAsync("/scalar/v1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("scalar");
    }

    [DockerRequiredFact]
    public async Task EntriesApi_OpenApiDocument_ContainsEntriesEndpoints()
    {
        var client = _entriesFactory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/entries", out _).ShouldBeTrue();
        paths.TryGetProperty("/api/entries/{id}", out _).ShouldBeTrue();
    }

    [DockerRequiredFact]
    public async Task ConsolidationApi_SwaggerUi_Returns200AndHtml()
    {
        var client = _consolidationFactory.CreateClient();

        var response = await client.GetAsync("/swagger");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("Swagger UI");
    }

    [DockerRequiredFact]
    public async Task ConsolidationApi_ScalarUi_Returns200AndHtml()
    {
        var client = _consolidationFactory.CreateClient();

        var response = await client.GetAsync("/scalar/v1");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.ShouldBe("text/html");
        var content = await response.Content.ReadAsStringAsync();
        content.ShouldContain("scalar");
    }

    [DockerRequiredFact]
    public async Task ConsolidationApi_OpenApiDocument_ContainsConsolidatedEndpoint()
    {
        var client = _consolidationFactory.CreateClient();

        var response = await client.GetAsync("/openapi/v1.json");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = doc.RootElement.GetProperty("paths");

        paths.TryGetProperty("/api/consolidated/{date}", out _).ShouldBeTrue();
    }

    public void Dispose()
    {
        _entriesFactory.Dispose();
        _consolidationFactory.Dispose();
    }
}
