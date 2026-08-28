using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class GetEntryEndpointsTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory = EntriesApiFactory.For(containers);
    private readonly HttpClient _client = EntriesApiFactory.For(containers).CreateClient();

    [DockerRequiredFact]
    public async Task GetById_AfterCreate_ReturnsSameEntry()
    {
        var created = await CreateEntryAsync(_client);

        var response = await _client.GetAsync($"/api/entries/{created.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EntryResponseTest>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(created.Id);
        body.Amount.ShouldBe(created.Amount);
    }

    [DockerRequiredFact]
    public async Task GetById_WithUnknownId_Returns404WithErrorCode()
    {
        var response = await _client.GetAsync($"/api/entries/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsTest>();
        problem.ShouldNotBeNull();
        problem.Extensions.TryGetValue("errorCode", out var errorCode).ShouldBeTrue();
        errorCode!.ToString().ShouldBe("ENTRY_NOT_FOUND");
    }

    [DockerRequiredFact]
    public async Task ListByDate_ReturnsOnlyEntriesOfRequestedDate()
    {
        var occurredAt = DateTime.UtcNow.AddDays(-3);
        await CreateEntryAsync(_client, 10m, "Credit", occurredAt);
        await CreateEntryAsync(_client, 4m, "Debit", occurredAt);
        await CreateEntryAsync(_client, 99m, "Credit", DateTime.UtcNow.AddDays(-1));

        var date = DateOnly.FromDateTime(occurredAt);
        var response = await _client.GetAsync($"/api/entries?date={date:yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<EntryResponseTest>>();
        body.ShouldNotBeNull();
        body.Count.ShouldBe(2);
        body.ShouldAllBe(entry =>
            DateOnly.FromDateTime(entry.OccurredAtUtc) == date);
    }

    [DockerRequiredFact]
    public async Task ListByDate_WithoutDate_Returns400()
    {
        var response = await _client.GetAsync("/api/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static async Task<EntryResponseTest> CreateEntryAsync(
        HttpClient client, decimal amount = 100m, string type = "Credit",
        DateTime? occurredAtUtc = null)
    {
        var request = new
        {
            amount,
            type,
            description = "Integration test entry",
            occurredAtUtc = occurredAtUtc ?? DateTime.UtcNow.AddMinutes(-5)
        };

        var response = await client.PostAsJsonAsync("/api/entries", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EntryResponseTest>())!;
    }

    private sealed record EntryResponseTest(
        Guid Id, decimal Amount, string Type, string Description,
        DateTime OccurredAtUtc, DateTime CreatedAtUtc);

    private sealed record ProblemDetailsTest
    {
        [JsonExtensionData]
        public Dictionary<string, object?> Extensions { get; init; } = [];
    }
}
