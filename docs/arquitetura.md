# Arquitetura

## Contexto

O sistema atende lojistas que precisam registrar entradas e saídas de caixa e consultar
o saldo consolidado por dia. Dois serviços independentes expõem APIs HTTP e se comunicam
por mensageria, garantindo que o registro de lançamentos nunca falhe por indisponibilidade
do serviço de consolidação ou do broker.

## Containers

```mermaid
flowchart LR
    Client[Client / Merchant]

    subgraph EntriesService[Entries Service]
        EA[Entries API :8080]
        OD[Outbox Dispatcher]
    end

    subgraph Broker[Message Broker]
        RM[RabbitMQ]
        DLQ[DLQ entry.created.dead]
    end

    subgraph ConsolidationService[Consolidation Service]
        CA[Consolidation API :8081]
        EC[EntryCreated Consumer]
    end

    DB[(MSSQL CashFlowDb)]

    Client -->|POST /api/entries| EA
    EA -->|entry + outbox in one transaction| DB
    OD -->|poll pending| DB
    OD -->|publish EntryCreated| RM
    RM -->|consume| EC
    RM -->|reject after 3 attempts| DLQ
    EC -->|project: processed_entries + daily_balances| DB
    Client -->|GET /api/consolidated/date| CA
    CA -->|read daily_balances| DB
```

## Fluxo de mensagens

```mermaid
sequenceDiagram
    participant C as Client
    participant E as Entries API
    participant DB as CashFlowDb
    participant D as Outbox Dispatcher
    participant R as RabbitMQ
    participant S as Consolidation Consumer
    participant P as daily_balances

    C->>E: POST /api/entries
    E->>DB: INSERT entry + outbox_message (single transaction)
    E-->>C: 201 Created
    D->>DB: SELECT pending outbox (every 2s)
    D->>R: publish EntryCreated (publisher confirm)
    D->>DB: UPDATE processed_at_utc
    R->>S: deliver entry.created (prefetch 10)
    S->>S: deserialize + retry ×3
    S->>DB: MERGE daily_balances + INSERT processed_entries (single transaction)
    S->>R: basicAck
    C->>S: GET /api/consolidated/{date}
    S-->>C: 200 totals + day balance + accumulated
```

## Modelo de dados (ERD)

```mermaid
erDiagram
    entries ||..o{ outbox_messages : "created in same transaction"
    processed_entries ||..|| daily_balances : "updated in same transaction"

    entries {
        uniqueidentifier id PK
        decimal amount
        tinyint type
        nvarchar description
        datetime2 occurred_at_utc
        datetime2 created_at_utc
    }
    outbox_messages {
        uniqueidentifier id PK
        nvarchar type
        nvarchar payload
        datetime2 occurred_at_utc
        datetime2 processed_at_utc
    }
    processed_entries {
        uniqueidentifier entry_id PK
        datetime2 processed_at_utc
    }
    daily_balances {
        date date PK
        decimal total_credits
        decimal total_debits
        datetime2 updated_at_utc
    }
```

## Resiliência

- **Broker fora**: o POST retorna 201; o evento fica retido em `outbox_messages`
  (processed_at_utc IS NULL). O dispatcher tenta publicar a cada 2s e loga erros sem
  derrubar o worker. Ao recuperar o broker, as mensagens são publicadas.
- **Poison message** (JSON inválido): enviada imediatamente para a DLQ
  (`entry.created.dead`) sem retry nem projeção.
- **Falha de projeção** (3 tentativas): após 3 tentativas com backoff linear, a mensagem
  vai para a DLQ. As tentativas anteriores que falham não causam projeção parcial
  (transação rollback).
- **Consolidação fora**: o broker bufferiza as mensagens (unacked volta para a fila).
  Quando a Consolidation reinicia, o consumer reconsome.
- **Reprocessamento da DLQ**: manual via RabbitMQ Management UI ou shovel
  (`rabbitmqadmin` / CLI).
