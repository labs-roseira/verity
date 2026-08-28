# Planos de Execução — Verity Cash Flow

> **Desafio:** Sistema de Fluxo de Caixa (Desafio Desenvolvedor Backend — ago/26).
> Estes planos são a especificação executável do projeto: todo o código de cada onda
> está embarcado nos MDs. A implementação segue a ordem abaixo, onda por onda,
> com TDD (vermelho → verde → refactor).

## Como usar estes planos

1. Leia [`00-conventions.md`](00-conventions.md) antes de tudo — vale para todas as ondas.
2. [`contracts.md`](contracts.md) é a fonte única da verdade para contratos (evento, schemas SQL,
   ports, HTTP, portas, topologia RabbitMQ). Qualquer mudança de contrato passa obrigatoriamente por ele.
3. Execute as ondas em ordem. Cada MD traz: objetivo, pré-requisitos, arquivos a criar,
   fase RED (testes completos), fase GREEN (implementação completa), comandos de verificação,
   critérios de aceite e notas/riscos.
4. Toda onda termina com `dotnet build` limpo e `dotnet test` verde (unitários sempre;
   integração exige Docker e faz auto-skip sem ele).

## Arquitetura consolidada

```
src/
├── Verity.CashFlow.Domain/                      # Entities, VOs, Results (zero dependências)
├── Verity.CashFlow.Application/                 # Casos de uso, ports, IntegrationEvents
├── Verity.CashFlow.Infrastructure.Persistence/  # Dapper, MSSQL (CashFlowDb)
├── Verity.CashFlow.Infrastructure.Messaging/    # RabbitMQ: outbox dispatcher, publisher, consumer
├── Verity.CashFlow.Entries.Api/                 # :8080 — lançamentos
└── Verity.CashFlow.Consolidation.Api/           # :8081 — saldo consolidado
tests/
├── Verity.CashFlow.UnitTests/
└── Verity.CashFlow.IntegrationTests/
```

**Dependências:** `Api(s) → Persistence/Messaging → Application → Domain` (aciclo).

```
POST /api/entries (8080) ──▶ transação: entries + outbox ──▶ 201
                                   │
                    dispatcher (2s) ▼ publisher confirms
                              RabbitMQ (entry.created)
                                   │ consumer (prefetch 10, retry ×3)
                                   ▼
              projeção idempotente → daily_balances
                                   │
GET /api/consolidated/{data} (8081) ◀────────────┘
              falhas ──▶ DLQ (entry.created.dead)
```

## Ordem de execução

| Onda | Plano | Produz | Depende de | Verificação |
|---|---|---|---|---|
| 1 | [01-scaffold.md](01-scaffold.md) | Solution + 8 projetos + pacotes + `.editorconfig`/`.gitignore` | 00 | `dotnet build` |
| 2 | [02-entries-domain.md](02-entries-domain.md) | `Result<T>`, `Entry`, `EntryType`, `EntryErrors` | 1 | `dotnet test` (unit) |
| 3 | [03-entries-application.md](03-entries-application.md) | Casos de uso + `IEntryStore` + `EntryCreated` | 2 | `dotnet test` (unit) |
| 4 | [04-outbox-dispatcher.md](04-outbox-dispatcher.md) | Ports de mensageria + `OutboxDispatcher` + publisher | 3 | `dotnet test` (unit) |
| 5 | [05-consolidation-consumer.md](05-consolidation-consumer.md) | Processor + topologia + consumer + projeção Dapper | 4 | `dotnet test` (unit) |
| 6 | [06-consolidation-report.md](06-consolidation-report.md) | Relatório diário consolidado | 5 | `dotnet test` (unit) |
| 7 | [07-integration-tests.md](07-integration-tests.md) | Stores Dapper + endpoints + testes de integração/e2e | 1–6 | `dotnet test` (Docker) |
| 8 | [08-docker-docs.md](08-docker-docs.md) | Compose + Dockerfiles + README + arquitetura + 9 ADRs | 1–7 | `docker compose up` |

Contratos: [contracts.md](contracts.md) — leitura obrigatória antes das ondas 2, 4, 5, 6 e 7.

## Decisões-chave (validadas com o owner)

| Tema | Decisão |
|---|---|
| Arquitetura | Clean Architecture sem cross-cutting: `Domain` + `Application` compartilhados; **2 APIs** isoladas (Entries 8080, Consolidation 8081) |
| Infraestrutura | Separada por responsabilidade: `Infrastructure.Persistence` (Dapper/MSSQL) + `Infrastructure.Messaging` (RabbitMQ) |
| Banco de dados | **1 MSSQL** (CashFlowDb, 4 tabelas por módulo) — isolamento lógico; split futuro documentado no ADR-0003 |
| Integração | RabbitMQ com **Outbox transacional** (zero perda de eventos), consumidor com retry ×3 + DLQ + idempotência |
| Resultados | Padrão **`Result<T>` caseiro** no Domain — falhas esperadas não usam exceções |
| Estilo | SOLID com referência [EduardoPires/SOLID](https://github.com/EduardoPires/SOLID/tree/master/EP.SOLID) |
| Testes | xUnit + NSubstitute + **Shouldly** (BSD-3); Testcontainers (integração); TDD por ondas |
| Resiliência | Entries responde 201 mesmo com broker/consolidação fora (outbox retém); alvo 50 req/s, perda 0% |
| Idioma | Código/identificadores/commits: **inglês**. Documentação: **pt-BR**. Conversas: pt-BR |
| Docker | `docker-compose.yml` sobe 4 containers (2 APIs + MSSQL + RabbitMQ) |

## Estado do ambiente (verificado em 27/08/2026)

- ✔ .NET 10 SDK (10.0.400)
- ✔ Docker Desktop (Docker 29.7.2 + Compose v5.4.0) — ondas 7 (integração) e 8 (compose) liberadas
- ✔ Microsoft Learn MCP Server e csharp-ls planejados nas convenções (instalação do csharp-ls na onda 1)

## Status de execução (27/08/2026)

- ✔ Ondas 1–8 implementadas (scaffold, domain, application, outbox, consumer, relatório, testes, docker/docs)
- ✔ `dotnet build` limpo — 0 warnings, 0 erros
- ✔ `dotnet test` verde — 44 unitários + 11 integração = **55 testes** (com Docker ativo)
- ✔ `docker-compose.yml` + 2 Dockerfiles + README + arquitetura + 9 ADRs

### Bugs corrigidos na retomada (27/08/2026)

| Bug | Causa | Fix |
|---|---|---|
| Integração: timeout TCP no MSSQL | `WebApplicationFactory` não injeta config via `ConfigureAppConfiguration` no minimal hosting; app caía no `appsettings.json` (`localhost:1433`) | Factories passam config via **env vars** (`ConnectionStrings__`, `RabbitMq__`) |
| Dapper: `DateOnly` não suportado como parâmetro | Dapper 2.1.79 não conhece `DateOnly` | Conversão `date.ToDateTime(TimeOnly.MinValue)` em `DapperConsolidatedBalanceReader` e `DapperEntryProjection` |
| Dapper: `SnapshotRow` não deserializa `DateOnly` | `DATE` SQL → `DateTime`, Dapper não converte para `DateOnly` | `SnapshotRow.Date` trocado para `DateTime`, conversão manual via `DateOnly.FromDateTime` |
| Dapper: `EntryRow` não materializa | Colunas snake_case (`occurred_at_utc`) ≠ props PascalCase (`OccurredAtUtc`) | Aliases no SQL: `occurred_at_utc AS OccurredAtUtc` |
| Testes: `errorCode` ausente em ProblemDetails | `ProblemDetailsTest` sem `[JsonExtensionData]` | Adicionado `[JsonExtensionData]` em `CreateEntryEndpointTests` e `GetEntryEndpointTests` |
| Testes: `Location.AbsolutePath` falha em URI relativa | `Results.Created` com path relativo gera `UriKind.Relative` | Trocado para `.ToString()` |
| E2E: RabbitMQ `ACCESS_REFUSED` | `guest/guest` rejeitado via Docker port mapping (loopback restriction) | `CashFlowContainers` expõe credenciais do container via `GetConnectionString()`; factories passam via env vars |

## Pendências do usuário

1. ~~Instalar Docker Desktop antes da onda 7.~~ ✔ Concluído em 27/08/2026.
2. Criar o repositório GitHub público ao final (commits apenas sob pedido explícito).
3. O projeto template `Verity.Test.API` existente permanece intocado; a nova solution
   `Verity.CashFlow.sln` convive no mesmo diretório. Remoção do template fica a critério do owner.
