# Contratos — Fonte Única da Verdade

> Qualquer mudança nestes contratos atualiza este arquivo primeiro; as ondas
> referenciam-no e nunca divergem dele.

## 1. Nomes e portas fixos

| Recurso | Nome | Porta |
|---|---|---|
| Entries API | `verity-entries-api` (container) | 8080 |
| Consolidation API | `verity-consolidation-api` (container) | 8081 |
| MSSQL (banco único) | `mssql` — banco **CashFlowDb** | 1433 (host) / 1433 (interno) |
| RabbitMQ (AMQP) | `rabbitmq` | 5672 |
| RabbitMQ Management | UI web | 15672 |

Credenciais de demonstração: MSSQL `sa` / `Verity!CashFlow2026`.
RabbitMQ local (dotnet run): `verity` / `VerityRabbitMq2026` (mesmo usuario do compose;
o `guest` do RabbitMQ e restrito a conexoes localhost real e e rejeitado via Docker
port mapping).

## 2. Contratos de código

### Domain (`Verity.CashFlow.Domain`)

```csharp
namespace Verity.CashFlow.Domain.Entries;

public enum EntryType
{
    Credit = 1,
    Debit = 2
}
```

```csharp
namespace Verity.CashFlow.Domain.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
```

`Result` / `Result<T>` — ver código completo na Wave 2.

### Application (`Verity.CashFlow.Application`)

```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record EntryCreated(
    Guid EntryId,
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime OccurredAtUtc);
```

```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public static class EventTypes
{
    public const string EntryCreated = "EntryCreated";
}
```

**Serialização JSON do evento** (publisher e consumer usam as MESMAS opções):

```csharp
new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }
```

- `Type` serializa como string (`"Credit"` / `"Debit"`), não número.
- `Amount` é `decimal` (número JSON); datas em UTC ISO-8601.

**Exemplo de payload publicado:**

```json
{
  "entryId": "3f219b01-2d4c-4f0e-9a5b-1c6a5d9e8a10",
  "amount": 150.75,
  "type": "Credit",
  "description": "Cash sale",
  "occurredAtUtc": "2026-08-25T14:30:00"
}
```

## 3. Schema SQL — banco único CashFlowDb (4 tabelas)

```sql
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
```

`type`: `1 = Credit`, `2 = Debit` (mesmos valores do enum `EntryType`).

```sql
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
```

```sql
IF OBJECT_ID(N'dbo.processed_entries') IS NULL
BEGIN
    CREATE TABLE dbo.processed_entries
    (
        entry_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_processed_entries PRIMARY KEY,
        processed_at_utc  DATETIME2        NOT NULL
    );
END;
```

```sql
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
```

**Isolamento lógico por módulo** (mesmo banco físico):
`entries` + `outbox_messages` = módulo Entries · `processed_entries` + `daily_balances`
= módulo Consolidation. A Consolidation **nunca lê** `entries` — apenas sua projeção.

**Upsert de projeção (MERGE):**

```sql
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
```

## 4. Topologia RabbitMQ

| Objeto | Tipo | Nome | Observações |
|---|---|---|---|
| Exchange principal | direct (durable) | `entries.events` | Declarada pelo publisher e pela topologia do consumer |
| Fila principal | durable | `entry.created` | Argumento `x-dead-letter-exchange = entries.events.dlx`; declarada pelo consumer |
| Binding principal | — | `entries.events` → `entry.created` | routing key `entry.created` |
| Exchange DLX | direct (durable) | `entries.events.dlx` | Declarada pelo consumer |
| Fila DLQ | durable | `entry.created.dead` | Sem DLX (evita loop de poison); binding `entries.events.dlx` rk `entry.created` |

- Publisher confirma entrega (publisher confirms) e publica com `persistent = true`,
  `mandatory = true`, `ContentType = application/json`, `Type = "EntryCreated"`.
- Consumer: `prefetchCount = 10`, ack manual. Mensagem rejeitada após 3 tentativas
  (`BasicReject requeue: false`) vai para a DLQ.
- Reprocesso da DLQ: manual (management UI / shovel) — documentado no README.

## 5. Ports (interfaces na Application)

### Application/Entries

```csharp
namespace Verity.CashFlow.Application.Entries;

public interface IEntryStore
{
    Task SaveWithOutboxAsync(Entry entry, EntryCreated @event, CancellationToken cancellationToken);

    Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Entry>> ListByDateAsync(DateOnly date, int page, int pageSize, CancellationToken cancellationToken);
}
```

### Application/Consolidation

```csharp
namespace Verity.CashFlow.Application.Consolidation;

public interface IEntryProjection
{
    Task<bool> ApplyAsync(EntryCreated @event, CancellationToken cancellationToken);
}
```

```csharp
namespace Verity.CashFlow.Application.Consolidation;

public interface IConsolidatedBalanceReader
{
    Task<DailyBalanceSnapshot?> GetByDateAsync(DateOnly date, CancellationToken cancellationToken);

    Task<decimal> GetAccumulatedBalanceAsync(DateOnly upToDate, CancellationToken cancellationToken);
}
```

### Application/IntegrationEvents

```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IOutboxStore
{
    Task<IReadOnlyList<PendingOutboxMessage>> GetPendingAsync(int batchSize, CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);
}
```

```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken cancellationToken);
}
```

```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record PendingOutboxMessage(Guid Id, string Type, string Payload);
```

## 6. Contratos HTTP

### 6.1 Entries API (8080)

**POST /api/entries** — registra lançamento.

Request:
```json
{
  "amount": 150.75,
  "type": "Credit",
  "description": "Cash sale",
  "occurredAtUtc": "2026-08-25T14:30:00"
}
```

`amount` > 0; `type` `"Credit"` | `"Debit"`; `description` 1–500 chars;
`occurredAtUtc` opcional (default: agora UTC; não pode ser futuro).

Responses:
- `201` + `Location: /api/entries/{id}`
```json
{
  "id": "3f219b01-2d4c-4f0e-9a5b-1c6a5d9e8a10",
  "amount": 150.75,
  "type": "Credit",
  "description": "Cash sale",
  "occurredAtUtc": "2026-08-25T14:30:00",
  "createdAtUtc": "2026-08-26T12:00:00"
}
```
- `400` ProblemDetails com `errorCode` (ver tabela de códigos nas convenções, seção 9).

**GET /api/entries/{id}** — `200 EntryResponse` | `404` ProblemDetails (`ENTRY_NOT_FOUND`).

**GET /api/entries?date=yyyy-MM-dd&page=1&pageSize=20** — `200 [EntryResponse]` |
`400 ValidationProblem` (`date` obrigatório; `page` ≥ 1; `1 ≤ pageSize ≤ 100`).

**GET /health** — `200 {"status":"Ok"}`.

### 6.2 Consolidation API (8081)

**GET /api/consolidated/{date}** (yyyy-MM-dd) — saldo consolidado do dia.

Response `200` (dia sem dados → zeros):
```json
{
  "date": "2026-08-25",
  "totalCredits": 150.75,
  "totalDebits": 30.00,
  "dayBalance": 120.75,
  "accumulatedBalance": 120.75
}
```

**GET /health** — `200 {"status":"Ok"}`.

### 6.3 Configuração (appsettings / env)

| Chave | Escopo | Exemplo |
|---|---|---|
| `ConnectionStrings:CashFlowDatabase` | Ambas as APIs (mesmo banco) | `Server=localhost,1433;Database=CashFlowDb;...` |
| `RabbitMq:HostName/Port/UserName/Password` | Ambas | `localhost`, `5672`, `guest`, `guest` |

Env vars no compose: `ConnectionStrings__CashFlowDatabase`, `RabbitMq__HostName`, etc.

## 7. Regras de negócio consolidadas

1. Lançamento é imutável; correção = lançamento novo (estorno).
2. `amount` sempre positivo; o sinal é dado pelo `type` (Credit soma, Debit subtrai).
3. Dia sem lançamentos → relatório com zeros (`200`), não `404`.
4. Evento duplicado na fila não altera saldo (idempotência por `EntryId`).
5. Saldo acumulado = soma de `total_credits - total_debits` de todos os dias ≤ data.
6. Entries API responde `201` mesmo com RabbitMQ/Consolidação indisponíveis (outbox retém).
