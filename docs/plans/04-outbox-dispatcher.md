# Wave 4 — Outbox Dispatcher + Publisher RabbitMQ (TDD)

## Objetivo

Implementar os ports de mensageria na Application (`IOutboxStore`, `IEventPublisher`,
`PendingOutboxMessage`) e, na Infrastructure.Messaging, o `OutboxDispatcher`
(BackgroundService que publica eventos pendentes com publisher confirms e marca como
processados) e o `RabbitMqEventPublisher` (validado na integração, onda 7).

## Pré-requisitos

- Onda 3 concluída.
- `contracts.md` seções 4 (topologia) e 5 (ports de IntegrationEvents) lidas.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `tests/Verity.CashFlow.UnitTests/Messaging/OutboxDispatcherTests.cs` | RED |
| `src/Verity.CashFlow.Application/IntegrationEvents/PendingOutboxMessage.cs` | RED |
| `src/Verity.CashFlow.Application/IntegrationEvents/IOutboxStore.cs` | RED |
| `src/Verity.CashFlow.Application/IntegrationEvents/IEventPublisher.cs` | RED |
| `src/Verity.CashFlow.Infrastructure.Messaging/OutboxDispatcher.cs` | RED (stub) → GREEN |
| `src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqOptions.cs` | GREEN |
| `src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqEventPublisher.cs` | GREEN (integração) |

## Fase RED — testes + ports

### tests/Verity.CashFlow.UnitTests/Messaging/OutboxDispatcherTests.cs

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Infrastructure.Messaging;
using Xunit;

namespace Verity.CashFlow.UnitTests.Messaging;

public class OutboxDispatcherTests
{
    private readonly IOutboxStore _outboxStore = Substitute.For<IOutboxStore>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();

    private OutboxDispatcher CreateSut() =>
        new(_outboxStore, _eventPublisher, NullLogger<OutboxDispatcher>.Instance);

    private static PendingOutboxMessage NewMessage(Guid id) =>
        new(id, "EntryCreated", """{"entryId":"00000000-0000-0000-0000-000000000001"}""");

    [Fact]
    public async Task DispatchPendingAsync_WithPendingMessages_PublishesAndMarksEachProcessed()
    {
        var first = NewMessage(Guid.NewGuid());
        var second = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { first, second });
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(first.Type, first.Payload,
            Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(second.Type, second.Payload,
            Arg.Any<CancellationToken>());
        await _outboxStore.Received(1).MarkProcessedAsync(first.Id, Arg.Any<CancellationToken>());
        await _outboxStore.Received(1).MarkProcessedAsync(second.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_WithEmptyBatch_PublishesNothing()
    {
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage>());
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!,
            default);
        await _outboxStore.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenPublisherFails_DoesNotMarkProcessed()
    {
        var message = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { message });
        _eventPublisher
            .WhenForAnyArgs(publisher => publisher.PublishAsync(default!, default!, default))
            .Do(_ => throw new InvalidOperationException("broker unavailable"));
        var sut = CreateSut();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.DispatchPendingAsync(CancellationToken.None));

        await _outboxStore.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenPublisherFailsOnSecondMessage_FirstIsStillProcessed()
    {
        var first = NewMessage(Guid.NewGuid());
        var second = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { first, second });
        _eventPublisher
            .When(publisher => publisher.PublishAsync(second.Type, second.Payload,
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("broker unavailable"));
        var sut = CreateSut();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.DispatchPendingAsync(CancellationToken.None));

        await _outboxStore.Received(1).MarkProcessedAsync(first.Id, Arg.Any<CancellationToken>());
        await _outboxStore.DidNotReceive().MarkProcessedAsync(second.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_RequestsConfiguredBatchSize()
    {
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage>());
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _outboxStore.Received(1).GetPendingAsync(OutboxDispatcher.BatchSize,
            Arg.Any<CancellationToken>());
    }
}
```

### Ports e stub (compilação)

**src/Verity.CashFlow.Application/IntegrationEvents/PendingOutboxMessage.cs**
```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record PendingOutboxMessage(Guid Id, string Type, string Payload);
```

**src/Verity.CashFlow.Application/IntegrationEvents/IOutboxStore.cs**
```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IOutboxStore
{
    Task<IReadOnlyList<PendingOutboxMessage>> GetPendingAsync(int batchSize,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);
}
```

**src/Verity.CashFlow.Application/IntegrationEvents/IEventPublisher.cs**
```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken cancellationToken);
}
```

**src/Verity.CashFlow.Infrastructure.Messaging/OutboxDispatcher.cs** (stub)
```csharp
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

    public Task DispatchPendingAsync(CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        throw new NotImplementedException();
}
```

## Fase GREEN — implementação

**src/Verity.CashFlow.Infrastructure.Messaging/OutboxDispatcher.cs**
```csharp
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
```

**src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqOptions.cs**
```csharp
namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string EntriesExchange { get; init; } = "entries.events";

    public string EntryCreatedRoutingKey { get; init; } = "entry.created";

    public string EntryCreatedQueue { get; init; } = "entry.created";

    public string DeadLetterExchange { get; init; } = "entries.events.dlx";

    public string DeadLetterQueue { get; init; } = "entry.created.dead";
}
```

**src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqEventPublisher.cs**
```csharp
using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class RabbitMqEventPublisher(
    IOptions<RabbitMqOptions> options) : IEventPublisher, IAsyncDisposable
{
    private readonly RabbitMqOptions _options = options.Value;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);
    private IConnection? _connection;
    private IChannel? _channel;

    public async Task PublishAsync(string type, string payload,
        CancellationToken cancellationToken)
    {
        var channel = await GetChannelAsync(cancellationToken).ConfigureAwait(false);

        var properties = new BasicProperties
        {
            ContentType = "application/json",
            Type = type,
            Persistent = true
        };

        await channel.BasicPublishAsync(
            exchange: _options.EntriesExchange,
            routingKey: _options.EntryCreatedRoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: Encoding.UTF8.GetBytes(payload),
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken cancellationToken)
    {
        if (_channel is { IsOpen: true })
            return _channel;

        await _connectionLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_channel is { IsOpen: true })
                return _channel;

            _connection ??= await CreateConnectionAsync(cancellationToken).ConfigureAwait(false);

            var channelOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true);

            _channel = await _connection.CreateChannelAsync(channelOptions, cancellationToken)
                .ConfigureAwait(false);

            await _channel.ExchangeDeclareAsync(
                exchange: _options.EntriesExchange,
                type: ExchangeType.Direct,
                durable: true,
                autoDelete: false,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return _channel;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    private async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken)
    {
        var factory = new ConnectionFactory
        {
            HostName = _options.HostName,
            Port = _options.Port,
            UserName = _options.UserName,
            Password = _options.Password,
            AutomaticRecoveryEnabled = true
        };

        return await factory.CreateConnectionAsync(cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask DisposeAsync()
    {
        if (_channel is not null)
            await _channel.DisposeAsync().ConfigureAwait(false);

        if (_connection is not null)
            await _connection.DisposeAsync().ConfigureAwait(false);

        _connectionLock.Dispose();
    }
}
```

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
```

## Critérios de aceite

1. 5 testes de `OutboxDispatcherTests` verdes (mais ondas anteriores).
2. Dispatcher: publica → marca processado, mensagem a mensagem; falha de publish mantém
   a mensagem pendente (reprocessada no próximo ciclo).
3. `PeriodicTimer` de 2s; exceções de ciclo são logadas, nunca derrubam o worker.
4. Publisher com publisher confirms + exchange declarada de forma idempotente.
5. Ports na Application; implementações na Infrastructure.Messaging (DIP).

## Notas / riscos

- ⚠️ **Validar API do RabbitMQ.Client v7** na implementação: `CreateChannelOptions`,
  `BasicPublishAsync` e `ExchangeDeclareAsync` — conferir assinaturas na doc oficial
  do cliente (github.com/rabbitmq/rabbitmq-dotnet-client) antes do GREEN.
- Conexão é lazy: a Entries API sobe e responde 201 mesmo com broker fora
  (o dispatcher só conecta quando há mensagem pendente) — requisito de resiliência.
- Melhoria futura (README): contador de tentativas por mensagem no outbox para não
  bloquear o lote com uma mensagem crônica.
- `GetPendingAsync` marca ordem por `occurred_at_utc` (FIFO) — SQL na onda 7.
- `RabbitMqOptions` já nasce completo (campos de publisher + consumer) para ser
  compartilhado com a onda 5.
