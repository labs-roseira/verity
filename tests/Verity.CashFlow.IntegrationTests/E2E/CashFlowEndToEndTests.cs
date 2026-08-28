using System.Net;
using System.Net.Http.Json;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.E2E;

[Collection("cash-flow")]
public sealed class CashFlowEndToEndTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _entries = EntriesApiFactory.For(containers);
    private readonly ConsolidationApiFactory _consolidation =
        ConsolidationApiFactory.For(containers);

    [DockerRequiredFact]
    public async Task FullFlow_EntryPosted_EndsUpInConsolidatedBalance()
    {
        var entriesClient = _entries.CreateClient();
        var consolidationClient = _consolidation.CreateClient();

        var targetDate = DateTime.UtcNow.AddMinutes(-10);

        await entriesClient.PostAsJsonAsync("/api/entries", new
        {
            amount = 150m,
            type = "Credit",
            description = "Cash sale",
            occurredAtUtc = targetDate
        });

        await entriesClient.PostAsJsonAsync("/api/entries", new
        {
            amount = 30m,
            type = "Debit",
            description = "Supplier payment",
            occurredAtUtc = targetDate
        });

        var date = DateOnly.FromDateTime(targetDate);

        var balance = await WaitForBalanceAsync(consolidationClient, date,
            expected => expected.TotalCredits == 150m && expected.TotalDebits == 30m);

        balance.ShouldNotBeNull();
        balance.DayBalance.ShouldBe(120m);
        balance.AccumulatedBalance.ShouldBe(120m);
    }

    [DockerRequiredFact]
    public async Task FullFlow_DayWithoutEntries_ReturnsZeros()
    {
        var consolidationClient = _consolidation.CreateClient();

        var response = await consolidationClient
            .GetAsync($"/api/consolidated/{new DateOnly(2000, 1, 1):yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var balance = await response.Content
            .ReadFromJsonAsync<ConsolidatedBalanceTest>();
        balance.ShouldNotBeNull();
        balance.TotalCredits.ShouldBe(0m);
        balance.TotalDebits.ShouldBe(0m);
        balance.DayBalance.ShouldBe(0m);
        balance.AccumulatedBalance.ShouldBe(0m);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _consolidation.Dispose();
    }

    private static async Task<ConsolidatedBalanceTest?> WaitForBalanceAsync(
        HttpClient client, DateOnly date,
        Func<ConsolidatedBalanceTest, bool> condition)
    {
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (!timeoutCancellation.IsCancellationRequested)
        {
            var response = await client.GetAsync(
                $"/api/consolidated/{date:yyyy-MM-dd}",
                timeoutCancellation.Token);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var balance = await response.Content
                    .ReadFromJsonAsync<ConsolidatedBalanceTest>(timeoutCancellation.Token);
                if (balance is not null && condition(balance))
                    return balance;
            }

            await Task.Delay(500, timeoutCancellation.Token);
        }

        return null;
    }

    private sealed record ConsolidatedBalanceTest(
        DateOnly Date,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal DayBalance,
        decimal AccumulatedBalance);
}
