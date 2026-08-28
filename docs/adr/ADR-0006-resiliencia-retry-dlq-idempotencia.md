# ADR-0006 — Retry, DLQ e idempotência

**Status:** Aceito

## Contexto

O consumer da Consolidation precisa lidar com mensagens inválidas (poison), falhas
transientes de projeção e duplicatas (redelivery).

## Decisão

- **Retry**: 3 tentativas com backoff linear (delay × attempt). `retryDelay` injetável
  (testes usam `TimeSpan.Zero`).
- **Poison message** (JSON inválido ou campos obrigatórios faltando): DLQ imediata,
  sem retry nem projeção.
- **DLQ**: `entry.created.dead` via DLX; reprocessamento manual via management UI.
- **Idempotência**: tabela `processed_entries` com PK = `entry_id`; a projeção verifica
  existência antes de inserir/atualizar — duplicatas retornam `false` e são acked.

## Alternativas consideradas

- **Retry infinito com requeue**: rejeitado — pode bloquear a fila indefinidamente.
- **Idempotência por timestamp/version**: rejeitada — EntryId é naturalmente único
  (Guid gerado pelo domínio).

## Consequências

**Prós:** alvo 50 req/s superado por desacoplamento + broker como buffer; perda 0%
(tolerância do desafio é ≤5%); poison messages não bloqueiam a fila.

**Contras:** sem contagem de tentativas persistida (in-memory no processor); sem
reprocessamento automático da DLQ (melhoria futura).
