using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class CreateEntryEndpointTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory = EntriesApiFactory.For(containers);
    private readonly HttpClient _client = EntriesApiFactory.For(containers).CreateClient();

    [DockerRequiredFact]
    public async Task Post_WithValidCredit_Returns201WithLocationAndBody()
    {
        var request = new
        {
            amount = 150.75m,
            type = "Credit",
            description = "Cash sale",
            occurredAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var body = await response.Content.ReadFromJsonAsync<EntryResponseTest>();
        body.ShouldNotBeNull();
        body.Amount.ShouldBe(150.75m);
        body.Type.ShouldBe("Credit");
        body.Description.ShouldBe("Cash sale");
        response.Headers.Location!.ToString().ShouldBe($"/api/entries/{body.Id}");
    }

    [DockerRequiredFact]
    public async Task Post_WithNonPositiveAmount_Returns400WithErrorCode()
    {
        var request = new
        {
            amount = 0m,
            type = "Credit",
            description = "Cash sale"
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsTest>();
        problem.ShouldNotBeNull();
        problem.Extensions.TryGetValue("errorCode", out var errorCode).ShouldBeTrue();
        errorCode!.ToString().ShouldBe("AMOUNT_MUST_BE_POSITIVE");
    }

    [DockerRequiredFact]
    public async Task Post_WithFutureOccurredAt_Returns400WithErrorCode()
    {
        var request = new
        {
            amount = 10m,
            type = "Debit",
            description = "Supplier payment",
            occurredAtUtc = DateTime.UtcNow.AddDays(1)
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    [DockerRequiredFact]
    public async Task Post_WithSameIdempotencyKey_Returns200WithOriginalEntry()
    {
        var key = Guid.NewGuid().ToString();
        var request = new
        {
            amount = 75m,
            type = "Credit",
            description = "Idempotent entry"
        };

        var req1 = new HttpRequestMessage(HttpMethod.Post, "/api/entries")
        {
            Content = JsonContent.Create(request)
        };
        req1.Headers.Add("Idempotency-Key", key);
        var resp1 = await _client.SendAsync(req1);
        resp1.StatusCode.ShouldBe(HttpStatusCode.Created);
        var body1 = await resp1.Content.ReadFromJsonAsync<EntryResponseTest>();
        body1.ShouldNotBeNull();

        var req2 = new HttpRequestMessage(HttpMethod.Post, "/api/entries")
        {
            Content = JsonContent.Create(request)
        };
        req2.Headers.Add("Idempotency-Key", key);
        var resp2 = await _client.SendAsync(req2);
        resp2.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body2 = await resp2.Content.ReadFromJsonAsync<EntryResponseTest>();
        body2.ShouldNotBeNull();
        body2.Id.ShouldBe(body1.Id);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
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
