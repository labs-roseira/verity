using System.Text.Json;
using Microsoft.Extensions.Logging;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Application.Entries;

public sealed class CreateEntryUseCase(
    IEntryStore entryStore,
    IEventPublisher eventPublisher,
    IOutboxStore outboxStore,
    TimeProvider clock,
    ILogger<CreateEntryUseCase> logger)
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

        var outboxId = await entryStore.SaveWithOutboxAsync(
            entry, @event, cancellationToken);

        await TryPublishInlineAsync(outboxId, @event, cancellationToken);

        return Result.Success(entry);
    }

    private async Task TryPublishInlineAsync(
        Guid outboxId, EntryCreated @event, CancellationToken cancellationToken)
    {
        try
        {
            var payload = JsonSerializer.Serialize(@event, IntegrationEventJsonOptions.Default);

            await eventPublisher.PublishAsync(EventTypes.EntryCreated, payload, cancellationToken)
                .ConfigureAwait(false);

            await outboxStore.MarkProcessedAsync(outboxId, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex,
                "Inline publish for outbox message {OutboxId} failed; dispatcher will retry.",
                outboxId);
        }
    }
}
