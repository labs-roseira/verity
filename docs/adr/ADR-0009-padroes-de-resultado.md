# ADR-0009 — Padrão Result&lt;T&gt; caseiro

**Status:** Aceito

## Contexto

Falhas esperadas (validação de domínio, recurso não encontrado, regras de negócio) não
devem lançar exceções — precisam ser valores tratáveis no fluxo normal. Exceções ficam
apenas para o inesperado (infraestrutura fora, JSON inválido na fila).

## Decisão

Implementar `Result`, `Result<T>` e `Error` no Domain (~60 linhas, zero dependências).
Factory methods do domínio retornam `Result<T>`; casos de uso propagam; endpoints
mapeiam `Error.Code` → ProblemDetails HTTP com `extensions["errorCode"]`.

## Alternativas consideradas

- **ErrorOr**: rejeitado — adiciona dependência para algo que 60 linhas resolvem.
- **FluentResults**: rejeitado — mesma razão, API mais verbosa.
- **Exceções de domínio**: rejeitadas — controle de fluxo por exceção é anti-pattern
  e dificulta testes.

## Consequências

**Prós:** zero dependências externas para o padrão; fluxo explícito (sem try/catch de
domínio); mapeamento `Error.Code` → HTTP é uma tabela simples.

**Contras:** `Result<T>.Value` lança `InvalidOperationException` em falha (bug de
programação, não fluxo) — exige disciplina do consumidor; sem agregação de múltiplos
erros (YAGNI — uma falha por operação de domínio).
