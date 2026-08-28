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
        CancellationToken ct)
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
            logger.LogWarning(ex, "Poison message; dead lettering.");
            return ProcessingDecision.DeadLetter;
        }

        var delay = retryDelay ?? DefaultRetryDelay;

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                if (await entryProjection.ApplyAsync(@event, ct).ConfigureAwait(false))
                    return ProcessingDecision.Acknowledge;

                logger.LogInformation("Duplicate entry {EntryId} ignored.", @event.EntryId);
                return ProcessingDecision.Acknowledge;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (attempt < MaxAttempts)
            {
                logger.LogWarning(ex, "Projection failed (attempt {Attempt}).", attempt);
                await Task.Delay(delay * attempt, ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to project entry {EntryId}; dead lettering.", @event.EntryId);
                return ProcessingDecision.DeadLetter;
            }
        }

        return ProcessingDecision.DeadLetter;
    }
}
