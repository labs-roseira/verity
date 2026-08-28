# ADR-0007 — Estratégia de testes

**Status:** Aceito

## Contexto

O projeto precisa de testes unitários rápidos (sem infraestrutura) e testes de integração
que validem o fluxo completo com dependências reais (MSSQL, RabbitMQ).

## Decisão

- **Unitários**: xUnit + NSubstitute (mocks de ports) + Shouldly (asserções). TDD por
  ondas (RED → GREEN → REFACTOR). Ports substituídos, jamais tipos de infraestrutura.
  `TimeProvider` injetado para determinismo.
- **Integração**: `WebApplicationFactory<Program>` + Testcontainers (MSSQL 2022 +
  RabbitMQ 3 reais). `[DockerRequiredFact]` auto-skip sem Docker — `dotnet test` sempre
  roda em qualquer máquina.
- **FluentAssertions excluída** (licença comercial); Shouldly escolhida (BSD-3-Clause,
  mensagens de falha descritivas, sintaxe natural).

## Alternativas consideradas

- **FluentAssertions**: rejeitada pela licença comercial (a partir de v8).
- **Moq**: rejeitado em favor de NSubstitute (sintaxe mais limpa, sem setup expressions).
- **MSTest/NUnit**: rejeitados — xUnit é padrão de facto em .NET moderno.

## Consequências

**Prós:** testes unitários em <1s; integração valida fluxo real (E2E com containers);
auto-skip garante CI sem Docker.

**Contras:** integração exige Docker Desktop (pode ser lento na primeira run — pull de
imagens); WebApplicationFactory com dois Program precisa de namespaces distintos
(resolvido com `namespace` block no Program.cs).
