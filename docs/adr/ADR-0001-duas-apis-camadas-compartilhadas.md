# ADR-0001 — Duas APIs com camadas compartilhadas

**Status:** Aceito

## Contexto

O desafio exige uma aplicação de lançamentos e outra de saldo consolidado. A separação
precisa garantir isolamento de deploy e de falha: o registro de lançamentos não pode falhar
por indisponibilidade da consolidação.

## Decisão

Cada requisito vira um serviço/processo independente (Entries API e Consolidation API).
Domain, Application, Infrastructure.Persistence e Infrastructure.Messaging são bibliotecas
compartilhadas — cada API referencia o que precisa e registra apenas os serviços que usa.

## Alternativas consideradas

- **API única com dois endpoints**: rejeitada — não atende o requisito de desacoplamento
  e falha conjunta.
- **Microsserviços com bancos separados**: viável mas adiciona complexidade de
  infraestrutura desnecessária para o escopo (ver ADR-0003).

## Consequências

**Prós:** deploy independente, falha isolada, cada API escala separadamente, reuso de
código de domínio/aplicação sem duplicação.

**Contras:** duas imagens Docker para manter, coordenação de versões de bibliotecas
compartilhadas.
