# Wave 0 — Convenções do Projeto

> Este documento é obrigatório para todas as ondas. Ele define idiomas, naming,
> estrutura de testes, TDD, ferramentas assistidas, SOLID, o padrão Result<T>
> e os princípios de simplicidade.

## 1. Idiomas

| Item | Idioma |
|---|---|
| Código, identificadores, mensagens de erro, nome de filas/exchanges/bancos | Inglês |
| Commits (Conventional Commits) | Inglês |
| Documentação (README, arquitetura, ADRs, planos) | Português (pt-BR) |
| Conversas com o owner | Português (pt-BR) |

## 2. Naming

| Elemento | Convenção | Exemplo |
|---|---|---|
| Classes, records, métodos, propriedades | PascalCase | `CreateEntryUseCase` |
| Parâmetros, variáveis locais | camelCase | `entryStore` |
| Interfaces | `I` + PascalCase | `IEntryStore` |
| Constantes | PascalCase | `DescriptionMaxLength` |
| Códigos de erro (`Error.Code`) | UPPER_SNAKE_CASE | `AMOUNT_MUST_BE_POSITIVE` |
| Testes | `Method_Scenario_ExpectedResult` | `Create_WithNonPositiveAmount_ReturnsFailure` |
| Arquivos de teste | `{ClasseSobTeste}Tests.cs` | `EntryTests.cs` |
| Tabelas / colunas SQL | snake_case | `outbox_messages.processed_at_utc` |
| Filas / exchanges RabbitMQ | ponto como separador | `entry.created`, `entries.events` |

## 3. Estrutura de código

- **Sem comentários** no código — nomes e testes explicam a intenção.
- `sealed` por padrão; `record` para DTOs e contratos imutáveis.
- Primary constructors (C# 12+) quando reduzir ruído.
- `static` em membros puros; expression-bodied para membros de uma linha.
- Todas as datas são **UTC** (`DateTimeKind.Utc`), sufixo `Utc` nos nomes.

### Pastas por projeto

| Projeto | Pastas |
|---|---|
| `Verity.CashFlow.Domain` | `Entries/`, `Consolidation/`, `Results/` |
| `Verity.CashFlow.Application` | `Entries/`, `Consolidation/`, `IntegrationEvents/` |
| `Verity.CashFlow.Infrastructure.Persistence` | raiz (6 arquivos coesos — sem subpastas) |
| `Verity.CashFlow.Infrastructure.Messaging` | raiz (7 arquivos coesos — sem subpastas) |
| `Verity.CashFlow.*.Api` | `Endpoints/`, `Program.cs` |
| `Verity.CashFlow.UnitTests` | `Domain/`, `Application/`, `Messaging/` |
| `Verity.CashFlow.IntegrationTests` | `Support/`, `Fixtures/`, `Entries/`, `E2E/` |

Ports vivem junto de quem consome (módulo da Application), nunca junto da
implementação (Infrastructure).

### Princípios de simplicidade (valem para todas as ondas)

| Princípio | Aplicação prática |
|---|---|
| Arquivos pequenos | 1 classe por arquivo; nenhum arquivo > ~120 linhas |
| API mínima | Ports com o menor número de métodos possível (`IEventPublisher` tem 1) |
| SQL explícito e curto | Constantes `const string` no topo da classe; sem query builders |
| Zero abstrações especulativas | Nada de generics/repos genéricos/UoW — só o que os testes exigem |
| DI enxuto | `AddSingleton/AddTransient` diretos no Program.cs; sem wrappers |
| Complexidade justificada | Cada parte móvel (outbox, DLQ, retry) tem ADR explicando o porquê |

## 4. Testes

- **Frameworks:** xUnit + NSubstitute + **Shouldly** (asserções).
  FluentAssertions excluído (licença comercial); Shouldly escolhido por ser BSD-3-Clause,
  gratuito, com mensagens de falha descritivas e sintaxe natural.
- **Layout AAA** com linhas em branco separando Arrange / Act / Assert.
- Um `Fact` por comportamento; `Theory` + `InlineData` para variações de dado.
- Ports são substituídos por NSubstitute; jamais mockar tipos de infraestrutura.
- Tempo: `TimeProvider` injetado; testes stubam `GetUtcNow()` (determinismo).
- Integração: `WebApplicationFactory<Program>` + Testcontainers (MSSQL e RabbitMQ reais).
- `[DockerRequiredFact]`: testes de integração pulam automaticamente sem Docker —
  `dotnet test` sempre roda em qualquer máquina.

### Estilo Shouldly

```csharp
entry.Amount.ShouldBe(100.50m);
result.IsFailure.ShouldBeTrue();
result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
entries.Count.ShouldBe(2);
Should.Throw<InvalidOperationException>(() => sut.Execute());
await Should.ThrowAsync<InvalidOperationException>(() => sut.ExecuteAsync());
```

## 5. TDD pragmático (por onda)

1. **RED:** escrever o lote de testes da onda + esqueletos mínimos
   (`NotImplementedException`) para o projeto compilar. Rodar: tudo falha (ou pula).
2. **GREEN:** implementar o mínimo que satisfaz os testes, usando o código dos MDs.
3. **REFACTOR:** revisar nomes/duplicação sem alterar comportamento; testes continuam verdes.
4. Ondas agrupam lotes (não um teste por vez) para manter momentum.

## 6. Ferramentas de desenvolvimento assistido

### 6.1 Microsoft Learn MCP Server

- Endpoint: `https://learn.microsoft.com/api/mcp` (HTTP, sem autenticação).
- Ferramentas: `microsoft_docs_search(query)`, `microsoft_docs_fetch(url)`,
  `microsoft_code_sample_search(query, language?)`.
- **Política de uso:** toda API nova ou específica de versão (.NET 10, Dapper,
  minimal APIs, `WebApplicationFactory`, Testcontainers, xUnit) é verificada no
  Learn MCP **antes** de entrar no código — nunca confiar apenas em memória de treino.
- **Cenários obrigatórios de consulta:** assinaturas de minimal API/OpenAPI no .NET 10,
  healthchecks, DI lifecycle, `TimeProvider`, binding de `DateOnly`, configuração
  `Microsoft.AspNetCore.Http.Json`.
- **Fora do escopo Microsoft** (RabbitMQ.Client, Dapper, Testcontainers): verificar na
  documentação oficial dos projetos (GitHub/docs) — anotar a fonte na wave.
- Decisões influenciadas por doc oficial citam a URL no ADR correspondente.
- **Fallback:** sem resposta útil → `microsoft_code_sample_search`; persistindo a dúvida →
  anotar em "Notas/riscos" da onda e decidir com o owner.

### 6.2 LSP C# (csharp-ls)

- Servidor Roslyn-based (`dotnet tool install --global csharp-ls`), alternativa leve ao OmniSharp.
- **Status:** ainda não instalado — instalação é pré-requisito da onda 1.
- Uso: diagnóstico rápido (erros/warnings) por arquivo após escrever cada item.
- **Ordem de verificação:** csharp-ls diagnostics → `dotnet build` → `dotnet test`.
- O LSP **não substitui** os comandos oficiais de verificação das ondas — apenas acelera
  o ciclo de feedback local.

### 6.3 Desambiguação

"LSP" neste projeto pode significar **Language Server Protocol** (ferramenta, seção 6.2)
ou **Liskov Substitution Principle** (design, seção 8). O contexto do documento define.

## 7. Tratamento de resultados — padrão Result<T>

### Princípio

- **Falhas esperadas** (validação de domínio, recurso não encontrado, regras de negócio)
  retornam `Result` / `Result<T>` — nunca lançam exceção.
- **Falhas inesperadas** (infraestrutura: banco fora, broker fora, JSON inválido na fila)
  permanecem como exceções — capturadas nos handlers/BackgroundServices, nunca engolidas.

### Tipos (`Verity.CashFlow.Domain.Results`)

- `Error(Code, Message)` — record imutável; `Error.None` para sucesso.
- `Result` — operação sem valor de retorno (`IsSuccess`/`IsFailure`/`Error`).
- `Result<T>` — operação com valor; `Value` acessível apenas em sucesso
  (acesso em falha lança `InvalidOperationException` — bug de programação, não fluxo).

### Fluxo do padrão

1. Domínio: factory methods retornam `Result<T>` com `Error` tipado
   (`Entry.Create` → `Result<Entry>`).
2. Casos de uso: propagam/compõem Results (`IsFailure` → `Result.Failure<T>(error)`).
3. Endpoints: pattern match `IsSuccess`/`IsFailure` → mapeia `Error.Code` para
   ProblemDetails HTTP (tabela da seção 9).

### Regras

- `Error.Code` em UPPER_SNAKE_CASE; mensagens em inglês.
- Uma falha por operação de domínio (sem agregação de múltiplos erros — YAGNI).
- `DomainException` **não existe** no projeto; exceções só para o inesperado.
- Testes: `result.IsFailure.ShouldBeTrue()` + `result.Error.Code.ShouldBe("...")`
  (substituem `Assert.Throws`).

## 8. Princípios SOLID — referência: EduardoPires/SOLID (EP.SOLID)

Repositório de referência: https://github.com/EduardoPires/SOLID/tree/master/EP.SOLID
Estrutura didática: cada princípio tem `*.Violacao` (como NÃO fazer) e `*.Solucao`
(como fazer). Nosso código segue o padrão das pastas `*.Solucao`.

| Princípio | Exemplo no EP.SOLID | Aplicação neste projeto |
|---|---|---|
| **S**RP | `ClienteServices` dividido em classes de única responsabilidade | 1 caso de uso = 1 classe (`CreateEntryUseCase`); entidade `Entry` só valida; endpoints finos (apenas HTTP + mapeamento de Result) |
| **O**CP | Novos serviços sem alterar `ClienteServices` | Novo tipo de lançamento = novo valor de enum, sem alterar consumidores; novo evento de integração = novo handler |
| **L**SP | Implementações substituíveis sem quebrar contrato | Toda implementação de port (Dapper, substitutes NSubstitute) honra exatamente o contrato; nenhum método lança `NotSupportedException` no caminho feliz (anti-exemplo: `Array` : `ICollection.Add`) |
| **I**SP | Interfaces pequenas e coesas em `Interfaces/` | Ports granulares (`IEntryStore`, `IEntryProjection`, `IConsolidatedBalanceReader`, `IEventPublisher` com 1 método); consumidor depende só do que usa |
| **D**IP | `ClienteServices` depende de `IClienteRepository`, não da implementação | Application define ports; Infrastructure implementa (Dapper, RabbitMQ); Api compõe via DI; caso de uso nunca referencia `SqlClient`/`RabbitMQ.Client` |

**Regras de verificação (code review de cada onda):**

1. Nenhum arquivo de Domain/Application referencia tipos de infraestrutura (Dapper, SqlClient, RabbitMQ.Client).
2. Toda dependência de caso de uso entra pelo construtor como interface.
3. Interfaces vivem junto de quem consome (Application), não de quem implementa (Infrastructure).
4. Se a classe tem "E" no resumo de responsabilidade ("salva E publica E valida"), dividir.

## 9. Tratamento de erros e contratos HTTP

| Situação | HTTP | Corpo |
|---|---|---|
| Criação com sucesso | 201 | `EntryResponse` + header `Location` |
| Bind inválido (JSON/tipos) | 400 | ProblemDetails (framework) |
| Falha esperada de domínio/negócio | 400 | ProblemDetails + `errorCode` |
| Recurso inexistente | 404 | ProblemDetails + `errorCode` |
| Erro inesperado | 500 | ProblemDetails (sem detalhes internos) |
| Relatório sem dados no dia | 200 | resposta com zeros (regra de negócio, não erro) |

### Tabela de códigos de erro (`Error.Code` → HTTP)

| Código | HTTP | Origem |
|---|---|---|
| `AMOUNT_MUST_BE_POSITIVE` | 400 | `Entry.Create` |
| `ENTRY_TYPE_INVALID` | 400 | `Entry.Create` |
| `DESCRIPTION_REQUIRED` | 400 | `Entry.Create` |
| `DESCRIPTION_TOO_LONG` | 400 | `Entry.Create` |
| `OCCURRED_AT_IN_FUTURE` | 400 | `Entry.Create` |
| `ENTRY_NOT_FOUND` | 404 | `GetEntryByIdUseCase` |

- Endpoints usam `TypedResults` + `Results<...>` (tipados, OpenAPI-friendly).
- OpenAPI descritivo: todo endpoint com `.WithSummary(...)` / `.WithDescription(...)`.
  **Sem** XML docs (`GenerateDocumentationFile` desligado).

## 10. Versionamento e execução

- Commits: Conventional Commits em inglês (`feat:`, `fix:`, `test:`, `docs:`, `refactor:`).
  Sugestão: 1 commit por onda concluída.
- **Nunca commitar/pushar sem pedido explícito do owner.**
- `git init` só quando o owner pedir (repo GitHub é responsabilidade dele).
- Comandos destrutivos (deletar arquivos/projetos) sempre confirmados antes.

## 11. Critérios transversais de aceite (todas as ondas)

1. `dotnet build Verity.CashFlow.sln` sem erros e sem warnings novos.
2. `dotnet test` verde para os unitários da onda (integração: auto-skip sem Docker).
3. Nenhum comentário adicionado ao código.
4. Regras de SOLID (seção 8) e simplicidade (seção 3) respeitadas no diff da onda.
5. Falhas esperadas retornam `Result<T>`; exceções apenas para o inesperado.
6. APIs novas usadas na onda foram verificadas (Learn MCP / doc oficial) quando aplicável.
