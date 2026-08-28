using RabbitMQ.Client;

namespace Verity.CashFlow.Infrastructure.Messaging;

public static class RabbitMqTopology
{
    public static async Task DeclareAsync(IChannel channel, RabbitMqOptions options,
        CancellationToken cancellationToken)
    {
        await channel.ExchangeDeclareAsync(
            exchange: options.DeadLetterExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: null,
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: options.DeadLetterQueue,
            exchange: options.DeadLetterExchange,
            routingKey: options.EntryCreatedRoutingKey,
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            exchange: options.EntriesExchange,
            type: ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            queue: options.EntryCreatedQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = options.DeadLetterExchange
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            queue: options.EntryCreatedQueue,
            exchange: options.EntriesExchange,
            routingKey: options.EntryCreatedRoutingKey,
            cancellationToken: cancellationToken);
    }
}
