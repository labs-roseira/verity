using System.Text.Json;
using Microsoft.Extensions.Logging;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class EntryCreatedProcessor(
    IEntryProjection entryProjection,
    ILogger<EntryCreatedProcessor> logger,
    TimeSpan? retryDelay = null)
{
    public const int MaxAttempts = 3;

    private static readonly TimeSpan DefaultRetryDelay = TimeSpan.FromMilliseconds(500);

    public async Task<ProcessingDecision> ProcessAsync(ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        EntryCreated @event;
        try
        {
            @event = JsonSerializer.Deserialize<EntryCreated>(body.Span, IntegrationEventJsonOptions.Default)
                     ?? throw new JsonException("Event payload is null.");

            if (@event.EntryId == Guid.Empty)
                throw new JsonException("Event payload is missing required fields.");
        }
        catch (JsonException ex)
        {
            logger.LogWarning(ex,
                "Poison message received; sending to dead letter queue.");
            return ProcessingDecision.DeadLetter;
        }

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                var applied = await entryProjection.ApplyAsync(@event, cancellationToken)
                    .ConfigureAwait(false);

                if (!applied)
                    logger.LogInformation("Duplicate entry {EntryId} ignored.",
                        @event.EntryId);

                return ProcessingDecision.Acknowledge;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt >= MaxAttempts)
                {
                    logger.LogError(ex,
                        "Failed to project entry {EntryId} after {Attempts} attempts; dead lettering.",
                        @event.EntryId, attempt);
                    return ProcessingDecision.DeadLetter;
                }

                logger.LogWarning(ex,
                    "Failed to project entry {EntryId} (attempt {Attempt} of {MaxAttempts}).",
                    @event.EntryId, attempt, MaxAttempts);

                var delay = retryDelay ?? DefaultRetryDelay;
                await Task.Delay(delay * attempt, cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
