# Verity CashFlow — Sistema de Fluxo de Caixa

Sistema de fluxo de caixa para lojistas: registro de créditos/débitos e saldo diário
consolidado. Duas aplicações desacopladas por mensageria (RabbitMQ) com banco único
compartilhado (MSSQL).

## Arquitetura

O sistema é composto por dois serviços independentes que se comunicam exclusivamente
por mensageria, garantindo resiliência e desacoplamento:

- **Entries API** (:8080) — registra lançamentos (crédito/débito) e publica eventos de
  integração via padrão Outbox transacional.
- **Consolidation API** (:8081) — consome eventos, projeta saldos diários e expõe o
  relatório consolidado.

Diagrama completo e fluxos em [docs/arquitetura.md](docs/arquitetura.md). Decisões
técnicas documentadas em [ADRs](docs/adr/).

## Stack

| Camada | Tecnologia |
|---|---|
| Runtime | .NET 10, C# |
| Data access | Dapper (SQL explícito) |
| Banco | Microsoft SQL Server 2022 |
| Mensageria | RabbitMQ 3 (client v7 async) |
| Containers | Docker, docker-compose |
| Testes | xUnit, NSubstitute, Shouldly, Testcontainers |

## Pré-requisitos

- **Docker Desktop** — necessário para rodar via docker-compose e para os testes de
  integração (Testcontainers).
- **.NET 10 SDK** — apenas para desenvolvimento local sem Docker.

## Como rodar

### Com Docker (recomendado)

```powershell
docker compose up -d --build
```

Verificar saúde:

```powershell
Invoke-RestMethod http://localhost:8080/health
Invoke-RestMethod http://localhost:8081/health
```

RabbitMQ Management UI: `http://localhost:15672` (usuário: `verity`, senha:
`VerityRabbitMq2026`).

Exemplo de uso:

```powershell
$body = '{"amount": 150.75, "type": "Credit", "description": "Cash sale"}'
Invoke-RestMethod -Method Post -Uri http://localhost:8080/api/entries `
    -ContentType "application/json" -Body $body

Invoke-RestMethod http://localhost:8081/api/consolidated/2026-08-26
```

Parar:

```powershell
docker compose down
```

### Sem Docker (dev local)

Necessário MSSQL e RabbitMQ acessíveis. Configurar connection string e RabbitMq em
`src/Verity.CashFlow.Entries.Api/appsettings.json` e
`src/Verity.CashFlow.Consolidation.Api/appsettings.json`.

```powershell
dotnet run --project src/Verity.CashFlow.Entries.Api
dotnet run --project src/Verity.CashFlow.Consolidation.Api
```

## Como funciona

1. **POST /api/entries** — a Entries API valida o lançamento, persiste entrada + evento
   outbox em uma única transação e retorna 201 (mesmo com broker fora).
2. **Outbox Dispatcher** — BackgroundService que a cada 2s publica eventos pendentes no
   RabbitMQ com publisher confirms e marca como processados.
3. **EntryCreated Consumer** — BackgroundService na Consolidation que consome eventos com
   prefetch 10, tenta até 3 vezes com backoff linear, e na falha envia para DLQ.
4. **Projeção** — insere em `processed_entries` (idempotência por EntryId) e atualiza
   `daily_balances` (MERGE) na mesma transação.
5. **GET /api/consolidated/{date}** — retorna totais de créditos/débitos, saldo do dia e
   saldo acumulado. Dia sem dados retorna zeros (200, não 404).

### Cenários de falha

| Cenário | Comportamento |
|---|---|
| Broker fora | POST retorna 201; evento fica retido no outbox; dispatcher publica ao recuperar |
| Consolidation fora | Broker bufferiza mensagens; consumer reconsome ao reiniciar |
| Poison message (JSON inválido) | DLQ imediata sem retry |
| Falha de projeção (3×) | DLQ após 3 tentativas com backoff |
| Reprocessamento da DLQ | Manual via RabbitMQ Management UI ou shovel |

## APIs

| Método | Endpoint | Descrição |
|---|---|---|
| POST | `/api/entries` | Registra um lançamento |
| GET | `/api/entries/{id}` | Busca lançamento por ID |
| GET | `/api/entries?date=yyyy-MM-dd&page=1&pageSize=20` | Lista lançamentos por data |
| GET | `/api/consolidated/{date}` | Saldo consolidado do dia |
| GET | `/health` | Liveness probe (ambas APIs) |

### POST /api/entries

```json
{
  "amount": 150.75,
  "type": "Credit",
  "description": "Cash sale",
  "occurredAtUtc": "2026-08-26T10:00:00Z"
}
```

`occurredAtUtc` é opcional (default: agora). `type`: `"Credit"` ou `"Debit"`.

### Resposta 201

```json
{
  "id": "guid",
  "amount": 150.75,
  "type": "Credit",
  "description": "Cash sale",
  "occurredAtUtc": "2026-08-26T10:00:00Z",
  "createdAtUtc": "2026-08-26T12:00:00Z"
}
```

### Erros de domínio (400/404)

```json
{
  "title": "Entry validation failed.",
  "status": 400,
  "errorCode": "AMOUNT_MUST_BE_POSITIVE",
  "detail": "Entry amount must be greater than zero."
}
```

## Testes

```powershell
dotnet test Verity.CashFlow.sln
```

- **Unitários** (44 testes): rodam sem Docker — xUnit + NSubstitute + Shouldly.
- **Integração** (11 testes): exigem Docker (Testcontainers sobe MSSQL + RabbitMQ reais);
  auto-skip sem Docker (`[DockerRequiredFact]`).

Só unitários:

```powershell
dotnet test tests/Verity.CashFlow.UnitTests
```

| Projeto | Conteúdo |
|---|---|
| `tests/Verity.CashFlow.UnitTests` | Domain, Application, Messaging |
| `tests/Verity.CashFlow.IntegrationTests` | Endpoints, E2E, Resiliência |

## Decisões técnicas

| ADR | Título |
|---|---|
| [ADR-0001](docs/adr/ADR-0001-duas-apis-camadas-compartilhadas.md) | Duas APIs com camadas compartilhadas |
| [ADR-0002](docs/adr/ADR-0002-dapper-data-access.md) | Dapper como data access |
| [ADR-0003](docs/adr/ADR-0003-banco-unico-por-modulo.md) | Banco único com tabelas por módulo |
| [ADR-0004](docs/adr/ADR-0004-rabbitmq-mensageria.md) | RabbitMQ na integração |
| [ADR-0005](docs/adr/ADR-0005-outbox-transacional.md) | Padrão Outbox transacional |
| [ADR-0006](docs/adr/ADR-0006-resiliencia-retry-dlq-idempotencia.md) | Retry, DLQ e idempotência |
| [ADR-0007](docs/adr/ADR-0007-estrategia-testes.md) | Estratégia de testes |
| [ADR-0008](docs/adr/ADR-0008-solid-referencia-epires.md) | SOLID com referência EP.SOLID |
| [ADR-0009](docs/adr/ADR-0009-padroes-de-resultado.md) | Padrão Result&lt;T&gt; caseiro |

## Melhorias futuras

- Autenticação (API key / JWT)
- OpenTelemetry + métricas (Prometheus/Grafana)
- CI/CD (GitHub Actions)
- Contagem de tentativas por mensagem no outbox
- Shovel/reprocessamento automático da DLQ
- Migração de schema (Flyway/DbUp)
- Idempotência no POST (chave de cliente)
- Rate limiting
- Split do banco único em 2 bancos por módulo

## Sobre o desafio

### Requisitos atendidos

- [x] API de lançamentos (POST, GET por ID, GET por data)
- [x] API de saldo consolidado por dia
- [x] Consolidado processado assincronamente
- [x] Dia sem dados retorna zeros (não erro)
- [x] Arquitetura desacoplada com mensageria
- [x] Resiliência: POST retorna 201 mesmo com broker fora
- [x] Zero perda de eventos (outbox transacional)

### Requisitos opcionais contemplados

- [x] Mensageria (RabbitMQ com DLX/DLQ, publisher confirms, prefetch)
- [x] Containers (Docker + docker-compose)
- [x] Diagramas de arquitetura (mermaid)
- [x] TDD (testes unitários + integração com Testcontainers)
