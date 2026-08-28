using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class EntryCreatedConsumer(
    IOptions<RabbitMqOptions> options,
    EntryCreatedProcessor processor,
    ILogger<EntryCreatedConsumer> logger) : BackgroundService
{
    private readonly RabbitMqOptions _options = options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ConsumeUntilFailedAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "RabbitMQ consumer failed; retrying connection in 5 seconds.");
                await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken)
                    .ConfigureAwait(false);
            }
        }
    }

    private async Task ConsumeUntilFailedAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true
        };

        await using var connection = await factory
            .CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using var channel = await connection
            .CreateChannelAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        await RabbitMqTopology.DeclareAsync(channel, _options, cancellationToken)
            .ConfigureAwait(false);

        await channel.BasicQosAsync(
            prefetchSize: 0,
            prefetchCount: 10,
            global: false,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, eventArgs) =>
        {
            var decision = await processor
                .ProcessAsync(eventArgs.Body, cancellationToken).ConfigureAwait(false);

            if (decision == ProcessingDecision.Acknowledge)
            {
                await channel.BasicAckAsync(eventArgs.DeliveryTag, multiple: false)
                    .ConfigureAwait(false);
            }
            else
            {
                await channel.BasicRejectAsync(eventArgs.DeliveryTag, requeue: false)
                    .ConfigureAwait(false);
            }
        };

        await channel.BasicConsumeAsync(
            queue: _options.EntryCreatedQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: cancellationToken).ConfigureAwait(false);

        await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
    }
}
