using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class ResilienceTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory =
        EntriesApiFactory.For(containers, rabbitHostOverride: "broker-unavailable");

    [DockerRequiredFact]
    public async Task Post_WithBrokerUnavailable_Returns201AndKeepsEventInOutbox()
    {
        var client = _factory.CreateClient();
        var request = new
        {
            amount = 200m,
            type = "Credit",
            description = "Broker down scenario"
        };

        var response = await client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var connection = new SqlConnection(containers.ConnectionString);
        var pending = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.outbox_messages WHERE processed_at_utc IS NULL");

        pending.ShouldBeGreaterThanOrEqualTo(1);
    }

    [DockerRequiredFact]
    public async Task Health_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public void Dispose() => _factory.Dispose();
}
