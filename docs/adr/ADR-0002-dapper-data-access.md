# ADR-0002 — Dapper como data access

**Status:** Aceito

## Contexto

O projeto precisa de acesso a MSSQL com MERGE (upsert de daily_balances), paginação
(OFFSET/FETCH) e controle explícito de transações. Performance é relevante (alvo 50 req/s).

## Decisão

Usar Dapper (micro-ORM) com SQL explícito em constantes `const string` no topo de cada
classe. Sem query builders, sem LINQ-to-SQL.

## Alternativas consideradas

- **EF Core**: rejeitado pelo peso/abstração — o desafio é direto e o SQL explícito dá
  controle total do MERGE e paginação com menos surpresas.
- **ADO.NET puro (SqlDataReader)**: rejeitado pela verbosidade — Dapper elimina o
  boilerplate de mapeamento mantendo SQL explícito.

## Consequências

**Prós:** SQL explícito e auditável, performance máxima, controle fino de transações,
zero mapeamento mágico.

**Contras:** sem migration tool embutido (schema gerenciado por DatabaseInitializer);
refatoração de colunas exige buscar nas constantes SQL manualmente.
