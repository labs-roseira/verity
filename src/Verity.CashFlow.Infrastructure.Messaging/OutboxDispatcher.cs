using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class OutboxDispatcher(
    IOutboxStore outboxStore,
    IEventPublisher eventPublisher,
    ILogger<OutboxDispatcher> logger) : BackgroundService
{
    public const int BatchSize = 50;
    public static readonly TimeSpan PollingInterval = TimeSpan.FromSeconds(2);

    public async Task DispatchPendingAsync(CancellationToken cancellationToken)
    {
        var pending = await outboxStore.GetPendingAsync(BatchSize, cancellationToken)
            .ConfigureAwait(false);

        foreach (var message in pending)
        {
            await eventPublisher.PublishAsync(message.Type, message.Payload, cancellationToken)
                .ConfigureAwait(false);

            await outboxStore.MarkProcessedAsync(message.Id, cancellationToken)
                .ConfigureAwait(false);

            logger.LogInformation("Outbox message {OutboxMessageId} published.",
                message.Id);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollingInterval);

        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
        {
            try
            {
                await DispatchPendingAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Outbox dispatch cycle failed.");
            }
        }
    }
}
