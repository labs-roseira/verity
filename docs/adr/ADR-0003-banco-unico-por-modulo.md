# ADR-0003 — Banco único com tabelas por módulo

**Status:** Aceito

## Contexto

As duas APIs precisam compartilhar dados: a Entries escreve `entries` e `outbox_messages`;
a Consolidation lê `outbox_messages` (via dispatcher) e escreve `processed_entries` e
`daily_balances`. Separar em dois bancos exigiria mensageria para qualquer consulta
cross-module ou duplicação de dados.

## Decisão

Um único MSSQL (CashFlowDb) com isolamento lógico: tabelas do módulo Entries
(`entries`, `outbox_messages`) e do módulo Consolidation (`processed_entries`,
`daily_balances`). Cada módulo só lê/escreve suas tabelas — a Consolidation nunca lê
`entries` diretamente, apenas `daily_balances` (projeção).

## Alternativas consideradas

- **Dois bancos separados**: adicionar complexidade de infraestrutura e sincronização
  sem benefício claro no escopo do desafio. A resiliência já é garantida pela mensageria.
- **Database-per-service com shared DB anti-pattern assumido**: seria a alternativa
  futura se o sistema crescer.

## Consequências

**Prós:** simplicidade de infraestrutura, transação outbox trivial (mesma conexão),
projeção idempotente sem coordenação distribuída.

**Contras:** ponto único de falha no banco (mitigado por resiliência da mensageria —
o NFR é sobre consolidação assíncrona, não sobre tolerância a falha de banco); split
futuro viável sem mudança de código de domínio (apenas connection strings e SQL).
