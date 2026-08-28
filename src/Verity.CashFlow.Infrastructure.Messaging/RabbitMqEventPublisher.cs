using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher(
    IOptions<RabbitMqOptions> options) : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _opts = options.Value;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(string type, string payload, CancellationToken ct)
    {
        var channel = await GetChannelAsync(ct);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = type,
            Persistent = true
        };

        await channel.BasicPublishAsync(
            _opts.EntriesExchange,
            _opts.EntryCreatedRoutingKey,
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(payload),
            ct);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel?.IsOpen is true)
            return _channel;

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel?.IsOpen is true)
                return _channel;

            _connection ??= await new ConnectionFactory
            {
                HostName = _opts.HostName,
                Port = _opts.Port,
                UserName = _opts.UserName,
                Password = _opts.Password,
                AutomaticRecoveryEnabled = true,
                RequestedConnectionTimeout = TimeSpan.FromSeconds(2)
            }.CreateConnectionAsync(ct);

            _channel = await _connection.CreateChannelAsync(
                new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true),
                ct);

            await _channel.ExchangeDeclareAsync(
                _opts.EntriesExchange,
                ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: ct);

            return _channel;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync();

        if (_connection is not null)
            await _connection.DisposeAsync();

        _lock.Dispose();
    }
}
