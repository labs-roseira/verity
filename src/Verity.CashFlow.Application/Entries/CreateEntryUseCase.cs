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
    public async Task<Result<CreateEntryResult>> ExecuteAsync(
        decimal amount,
        EntryType type,
        string description,
        DateTime? occurredAtUtc,
        string? idempotencyKey,
        CancellationToken cancellationToken)
    {
        if (idempotencyKey is not null)
        {
            var existing = await entryStore
                .GetByIdempotencyKeyAsync(idempotencyKey, cancellationToken)
                .ConfigureAwait(false);

            if (existing is not null)
                return Result.Success(new CreateEntryResult(existing, WasCreated: false));
        }

        var utcNow = clock.GetUtcNow().UtcDateTime;

        var entryResult = Entry.Create(amount, type, description,
            occurredAtUtc ?? utcNow.ToLocalTime(), utcNow);

        if (entryResult.IsFailure)
            return Result.Failure<CreateEntryResult>(entryResult.Error);

        var entry = entryResult.Value;

        var @event = new EntryCreated(
            entry.Id,
            entry.Amount,
            entry.Type,
            entry.Description,
            entry.OccurredAtUtc,
            IdempotencyKey: idempotencyKey);

        var outboxId = await entryStore.SaveWithOutboxAsync(
            entry, @event, idempotencyKey, cancellationToken);

        await TryPublishInlineAsync(outboxId, @event, cancellationToken);

        return Result.Success(new CreateEntryResult(entry, WasCreated: true));
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
