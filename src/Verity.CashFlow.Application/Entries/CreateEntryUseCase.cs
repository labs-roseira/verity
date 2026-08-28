using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Application.Entries;

public sealed class CreateEntryUseCase(IEntryStore entryStore, TimeProvider clock)
{
    public async Task<Result<Entry>> ExecuteAsync(
        decimal amount,
        EntryType type,
        string description,
        DateTime? occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var entryResult = Entry.Create(amount, type, description,
            occurredAtUtc ?? utcNow.ToLocalTime(), utcNow);

        if (entryResult.IsFailure)
            return Result.Failure<Entry>(entryResult.Error);

        var entry = entryResult.Value;

        var @event = new EntryCreated(
            entry.Id,
            entry.Amount,
            entry.Type,
            entry.Description,
            entry.OccurredAtUtc);

        await entryStore.SaveWithOutboxAsync(entry, @event, cancellationToken);

        return Result.Success(entry);
    }
}
