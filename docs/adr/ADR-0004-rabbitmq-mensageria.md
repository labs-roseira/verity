# ADR-0004 — RabbitMQ na integração

**Status:** Aceito

## Contexto

A integração entre Entries e Consolidation precisa ser assíncrona, com retry, dead letter
queue e publisher confirms. O desafio menciona mensageria como requisito opcional.

## Decisão

Usar RabbitMQ 3 com a cliente .NET v7 (API async: `IChannel`, `BasicPublishAsync`,
`CreateChannelAsync`). Topologia: exchange direct `entries.events`, fila `entry.created`
com DLX `entries.events.dlx` e DLQ `entry.created.dead`.

## Alternativas consideradas

- **Kafka**: rejeitado pelo peso operacional para o escopo (cluster, partitions,
  consumer groups vs. um RabbitMQ standalone).
- **Azure Service Bus / AWS SQS**: rejeitado por introduzir dependência de cloud
  (o desafio pede Docker).

## Consequências

**Prós:** DLX/DLQ nativos, publisher confirms, prefetch controlável, management UI
para depuração, lightweight com Docker.

**Contras:** RabbitMQ.Client v7 tem API async diferente da v6 — validada na doc oficial
(github.com/rabbitmq/rabbitmq-dotnet-client); `guest` não funciona entre containers
(usa usuário `verity` no compose).
