# ADR-0005 — Padrão Outbox transacional

**Status:** Aceito

## Contexto

A Entries API precisa garantir que o lançamento e o evento de integração sejam
persistidos atomicamente — se o broker estiver fora, o evento não pode se perder.

## Decisão

Padrão Outbox: a entrada e a mensagem outbox são inseridas na mesma transação SQL.
Um BackgroundService (`OutboxDispatcher`) faz polling a cada 2s das mensagens pendentes
(`processed_at_utc IS NULL`), publica no RabbitMQ com publisher confirms e marca como
processadas. Sem `IUnitOfWork` explícito — a atomicidade é expressa como uma única
operação do port `IEntryStore.SaveWithOutboxAsync`.

## Alternativas consideradas

- **Transactional Outbox com UoW explícito**: rejeitado por adicionar abstração
  desnecessária (YAGNI) — o Dapper controla a transação diretamente.
- **CDC (Change Data Capture)**: rejeitado pela complexidade de infraestrutura.
- **Síncrono (publish direto no POST)**: rejeitado — viola o requisito de resiliência
  (POST deve retornar 201 mesmo com broker fora).

## Consequências

**Prós:** zero perda de eventos, POST sempre 201 (mesmo broker fora), dispatcher é
reativo (conecta só quando há mensagem pendente).

**Contras:** latência de até 2s entre POST e publicação; sem contagem de tentativas
por mensagem (melhoria futura — uma mensagem crônica pode bloquear o lote).
