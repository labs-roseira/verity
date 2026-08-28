# Wave 5 — Consolidation: Processor, Topologia, Consumer e Projeção (TDD)

## Objetivo

Implementar o processamento de eventos `EntryCreated` na Consolidation:
`EntryCreatedProcessor` (desserialização, retry ×3 com backoff, decisão ack/DLQ,
idempotência delegada), a topologia RabbitMQ (fila + DLX + DLQ), o consumidor
BackgroundService e a projeção Dapper (`processed_entries` + `daily_balances` na mesma
transação) com `SqlConnectionFactory` e `DatabaseInitializer` (schema completo do
CashFlowDb — 4 tabelas) na Infrastructure.Persistence.

## Pré-requisitos

- Onda 4 concluída.
- `contracts.md` seções 3 (schema), 4 (topologia) e 5 lidas.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `tests/Verity.CashFlow.UnitTests/Messaging/EntryCreatedProcessorTests.cs` | RED |
| `src/Verity.CashFlow.Application/Consolidation/IEntryProjection.cs` | RED |
| `src/Verity.CashFlow.Infrastructure.Messaging/ProcessingDecision.cs` | RED |
| `src/Verity.CashFlow.Infrastructure.Messaging/EntryCreatedProcessor.cs` | RED (stub) → GREEN |
| `src/Verity.CashFlow.Infrastructure.Persistence/SqlConnectionFactory.cs` | GREEN |
| `src/Verity.CashFlow.Infrastructure.Persistence/DatabaseInitializer.cs` | GREEN |
| `src/Verity.CashFlow.Infrastructure.Persistence/DapperEntryProjection.cs` | GREEN |
| `src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqTopology.cs` | GREEN |
| `src/Verity.CashFlow.Infrastructure.Messaging/EntryCreatedConsumer.cs` | GREEN (integração) |

## Fase RED — testes + port

### tests/Verity.CashFlow.UnitTests/Messaging/EntryCreatedProcessorTests.cs

```csharp
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Infrastructure.Messaging;
using Xunit;

namespace Verity.CashFlow.UnitTests.Messaging;

public class EntryCreatedProcessorTests
{
    private readonly IEntryProjection _entryProjection = Substitute.For<IEntryProjection>();

    private EntryCreatedProcessor CreateSut() =>
        new(_entryProjection, NullLogger<EntryCreatedProcessor>.Instance,
            retryDelay: TimeSpan.Zero);

    private static ReadOnlyMemory<byte> Serialize(EntryCreated @event)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, options));
    }

    [Fact]
    public async Task ProcessAsync_WithNewEvent_ProjectsAndAcknowledges()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection.ApplyAsync(@event, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
    }

    [Fact]
    public async Task ProcessAsync_WithDuplicateEvent_AcknowledgesWithoutReprojection()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection.ApplyAsync(@event, Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
        await _entryProjection.Received(1).ApplyAsync(@event, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not a json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"entryId":"not-a-guid"}""")]
    public async Task ProcessAsync_WithInvalidPayload_DeadLettersWithoutProjection(string payload)
    {
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Encoding.UTF8.GetBytes(payload),
            CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.DeadLetter);
        await _entryProjection.DidNotReceiveWithAnyArgs().ApplyAsync(default!,
            default);
    }

    [Fact]
    public async Task ProcessAsync_WhenProjectionAlwaysFails_DeadLettersAfterMaxAttempts()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Debit, "Supplier payment",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection
            .WhenForAnyArgs(projection => projection.ApplyAsync(default!, default))
            .Do(_ => throw new InvalidOperationException("database down"));
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.DeadLetter);
        await _entryProjection.Received(EntryCreatedProcessor.MaxAttempts).ApplyAsync(
            @event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenProjectionFailsThenSucceeds_RetriesAndAcknowledges()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Debit, "Supplier payment",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        var attempts = 0;
        _entryProjection
            .WhenForAnyArgs(projection => projection.ApplyAsync(default!, default))
            .Do(_ =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("transient failure");
            });
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
        attempts.ShouldBe(3);
    }
}
```

### Port e stub (compilação)

**src/Verity.CashFlow.Application/Consolidation/IEntryProjection.cs**
```csharp
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Application.Consolidation;

public interface IEntryProjection
{
    Task<bool> ApplyAsync(EntryCreated @event, CancellationToken cancellationToken);
}
```

**src/Verity.CashFlow.Infrastructure.Messaging/ProcessingDecision.cs**
```csharp
namespace Verity.CashFlow.Infrastructure.Messaging;

public enum ProcessingDecision
{
    Acknowledge,
    DeadLetter
}
```

**src/Verity.CashFlow.Infrastructure.Messaging/EntryCreatedProcessor.cs** (stub)
```csharp
using Microsoft.Extensions.Logging;
using Verity.CashFlow.Application.Consolidation;

namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class EntryCreatedProcessor(
    IEntryProjection entryProjection,
    ILogger<EntryCreatedProcessor> logger,
    TimeSpan? retryDelay = null)
{
    public const int MaxAttempts = 3;

    public Task<ProcessingDecision> ProcessAsync(ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
```

## Fase GREEN — implementação

**src/Verity.CashFlow.Infrastructure.Messaging/EntryCreatedProcessor.cs**
```csharp
using System.Text.Json;
using System.Text.Json.Serialization;
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

    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<ProcessingDecision> ProcessAsync(ReadOnlyMemory<byte> body,
        CancellationToken cancellationToken)
    {
        EntryCreated @event;
        try
        {
            @event = JsonSerializer.Deserialize<EntryCreated>(body.Span, SerializerOptions)
                     ?? throw new JsonException("Event payload is null.");
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
```

**src/Verity.CashFlow.Infrastructure.Persistence/SqlConnectionFactory.cs**
```csharp
using Microsoft.Data.SqlClient;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class SqlConnectionFactory(string connectionString)
{
    public async Task<SqlConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqlConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }
}
```

**src/Verity.CashFlow.Infrastructure.Persistence/DatabaseInitializer.cs**
```csharp
using Dapper;
using Microsoft.Data.SqlClient;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DatabaseInitializer(string connectionString, string databaseName)
{
    private const string SchemaSql = """
        IF OBJECT_ID(N'dbo.entries') IS NULL
        BEGIN
            CREATE TABLE dbo.entries
            (
                id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_entries PRIMARY KEY,
                amount          DECIMAL(18, 2)   NOT NULL,
                type            TINYINT          NOT NULL,
                description     NVARCHAR(500)    NOT NULL,
                occurred_at_utc DATETIME2        NOT NULL,
                created_at_utc  DATETIME2        NOT NULL
            );

            CREATE INDEX IX_entries_occurred_at_utc ON dbo.entries (occurred_at_utc);
        END;

        IF OBJECT_ID(N'dbo.outbox_messages') IS NULL
        BEGIN
            CREATE TABLE dbo.outbox_messages
            (
                id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_outbox_messages PRIMARY KEY,
                type             NVARCHAR(200)    NOT NULL,
                payload          NVARCHAR(MAX)    NOT NULL,
                occurred_at_utc  DATETIME2        NOT NULL,
                processed_at_utc DATETIME2        NULL
            );

            CREATE INDEX IX_outbox_messages_pending
                ON dbo.outbox_messages (occurred_at_utc)
                WHERE processed_at_utc IS NULL;
        END;

        IF OBJECT_ID(N'dbo.processed_entries') IS NULL
        BEGIN
            CREATE TABLE dbo.processed_entries
            (
                entry_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_processed_entries PRIMARY KEY,
                processed_at_utc  DATETIME2        NOT NULL
            );
        END;

        IF OBJECT_ID(N'dbo.daily_balances') IS NULL
        BEGIN
            CREATE TABLE dbo.daily_balances
            (
                date            DATE           NOT NULL CONSTRAINT PK_daily_balances PRIMARY KEY,
                total_credits   DECIMAL(18, 2) NOT NULL,
                total_debits    DECIMAL(18, 2) NOT NULL,
                updated_at_utc  DATETIME2      NOT NULL
            );
        END;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };

        await using (var master = new SqlConnection(masterBuilder.ConnectionString))
        {
            await master.OpenAsync(cancellationToken);
            await master.ExecuteAsync(new CommandDefinition(
                $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}]",
                cancellationToken: cancellationToken));
        }

        var databaseBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName
        };

        await using (var database = new SqlConnection(databaseBuilder.ConnectionString))
        {
            await database.OpenAsync(cancellationToken);
            await database.ExecuteAsync(new CommandDefinition(
                SchemaSql, cancellationToken: cancellationToken));
        }
    }
}
```

**src/Verity.CashFlow.Infrastructure.Persistence/DapperEntryProjection.cs**
```csharp
using Dapper;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DapperEntryProjection(SqlConnectionFactory connectionFactory)
    : IEntryProjection
{
    private const string ExistsSql = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.processed_entries WHERE entry_id = @EntryId
        ) THEN 1 ELSE 0 END AS BIT);
        """;

    private const string InsertProcessedSql = """
        INSERT INTO dbo.processed_entries (entry_id, processed_at_utc)
        VALUES (@EntryId, SYSUTCDATETIME());
        """;

    private const string UpsertBalanceSql = """
        MERGE INTO dbo.daily_balances AS target
        USING (SELECT @Date AS [date]) AS source
        ON target.[date] = source.[date]
        WHEN MATCHED THEN
            UPDATE SET
                total_credits  = target.total_credits + @CreditDelta,
                total_debits   = target.total_debits  + @DebitDelta,
                updated_at_utc = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([date], total_credits, total_debits, updated_at_utc)
            VALUES (source.[date], @CreditDelta, @DebitDelta, SYSUTCDATETIME());
        """;

    public async Task<bool> ApplyAsync(EntryCreated @event,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await connectionFactory
            .OpenAsync(cancellationToken);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken);

        var alreadyProcessed = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(ExistsSql, new { @event.EntryId }, transaction,
                cancellationToken: cancellationToken));

        if (alreadyProcessed)
            return false;

        var date = DateOnly.FromDateTime(@event.OccurredAtUtc);
        var creditDelta = @event.Type == EntryType.Credit ? @event.Amount : 0m;
        var debitDelta = @event.Type == EntryType.Debit ? @event.Amount : 0m;

        await connection.ExecuteAsync(new CommandDefinition(InsertProcessedSql,
            new { @event.EntryId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(UpsertBalanceSql,
            new { Date = date, CreditDelta = creditDelta, DebitDelta = debitDelta },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
```

**src/Verity.CashFlow.Infrastructure.Messaging/RabbitMqTopology.cs**
```csharp
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
```

**src/Verity.CashFlow.Infrastructure.Messaging/EntryCreatedConsumer.cs**
```csharp
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
```

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
```

## Critérios de aceite

1. 6 testes de `EntryCreatedProcessorTests` verdes.
2. Poison message (JSON inválido) → DLQ imediata, sem projeção e sem retry.
3. Falha de projeção: até 3 tentativas com backoff linear; exaurido → DLQ.
4. Duplicado: ack sem reprocessar (idempotência por `EntryId`).
5. Projeção atômica: `processed_entries` + `daily_balances` na mesma transação.
6. Consumer com prefetch 10 e reconexão automática em loop de 5s.

## Notas / riscos

- ⚠️ Validar assinaturas RabbitMQ.Client v7 (`BasicConsumeAsync`, `ReceivedAsync`,
  `BasicQosAsync`) na doc oficial antes do GREEN.
- O consumidor declara toda a topologia (fila, DLX, DLQ, bindings) — a Consolidation
  funciona mesmo se subir antes da Entries API.
- O `retryDelay` é injetável: testes usam `TimeSpan.Zero` para velocidade.
- Cancelamento durante o delay propaga `OperationCanceledException` — o worker encerra
  e a mensagem volta por redelivery (unacked) — sem perda.
- `DatabaseInitializer` cria as **4 tabelas** (schema completo do banco único);
  rodando nas duas APIs é idempotente — quem chegar primeiro cria, o outro não faz nada.
- Reprocessamento da DLQ: manual via management UI/shovel (README, onda 8).
