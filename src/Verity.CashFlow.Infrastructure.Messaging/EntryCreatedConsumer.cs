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
    private readonly RabbitMqOptions _opts = options.Value;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await RunAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Consumer connection lost; retrying in 5s.");
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
    }

    private async Task RunAsync(CancellationToken ct)
    {
        var factory = new ConnectionFactory
        {
            HostName = _opts.HostName,
            Port = _opts.Port,
            UserName = _opts.UserName,
            Password = _opts.Password,
            AutomaticRecoveryEnabled = true,
            RequestedConnectionTimeout = TimeSpan.FromSeconds(2)
        };

        await using var connection = await factory.CreateConnectionAsync(ct);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

        await RabbitMqTopology.DeclareAsync(channel, _opts, ct);
        await channel.BasicQosAsync(0, 10, false, ct);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, ct);

        await channel.BasicConsumeAsync(_opts.EntryCreatedQueue, autoAck: false, consumer, ct);

        await Task.Delay(Timeout.Infinite, ct);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var decision = await processor.ProcessAsync(ea.Body, ct);

        if (decision == ProcessingDecision.Acknowledge)
            await channel.BasicAckAsync(ea.DeliveryTag, false);
        else
            await channel.BasicRejectAsync(ea.DeliveryTag, false);
    }
}
