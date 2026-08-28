# Wave 3 — Application Entries: Casos de Uso + Ports (TDD)

## Objetivo

Implementar os casos de uso da Entries API com ports (DIP): `CreateEntryUseCase`
(retorna `Result<Entry>` e persiste entrada + evento de outbox como uma única
operação), `GetEntryByIdUseCase` (`ENTRY_NOT_FOUND` tipado) e `ListEntriesByDateUseCase`.
Também cria o contrato `EntryCreated` em `IntegrationEvents`.

## Pré-requisitos

- Onda 2 concluída (domínio verde).
- `contracts.md` seções 2 e 5 (ports) lidas.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `tests/Verity.CashFlow.UnitTests/Application/CreateEntryUseCaseTests.cs` | RED |
| `tests/Verity.CashFlow.UnitTests/Application/GetEntryByIdUseCaseTests.cs` | RED |
| `tests/Verity.CashFlow.UnitTests/Application/ListEntriesByDateUseCaseTests.cs` | RED |
| `src/Verity.CashFlow.Application/IntegrationEvents/EntryCreated.cs` | RED |
| `src/Verity.CashFlow.Application/IntegrationEvents/EventTypes.cs` | RED |
| `src/Verity.CashFlow.Application/Entries/IEntryStore.cs` | RED |
| `src/Verity.CashFlow.Application/Entries/CreateEntryUseCase.cs` | RED (stub) → GREEN |
| `src/Verity.CashFlow.Application/Entries/GetEntryByIdUseCase.cs` | RED (stub) → GREEN |
| `src/Verity.CashFlow.Application/Entries/ListEntriesByDateUseCase.cs` | RED (stub) → GREEN |

## Fase RED — testes + contratos

### tests/Verity.CashFlow.UnitTests/Application/CreateEntryUseCaseTests.cs

```csharp
using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;
using Xunit;

namespace Verity.CashFlow.UnitTests.Application;

public class CreateEntryUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();
    private readonly TimeProvider _clock = Substitute.For<TimeProvider>();

    private CreateEntryUseCase CreateSut() => new(_entryStore, _clock);

    [Fact]
    public async Task ExecuteAsync_WithValidInput_PersistsEntryWithOutboxEvent()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        _clock.GetUtcNow().Returns(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(100m, EntryType.Credit, "Cash sale", null,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _entryStore.Received(1).SaveWithOutboxAsync(
            Arg.Is<Entry>(e =>
                e.Id == result.Value.Id &&
                e.Amount == 100m &&
                e.Type == EntryType.Credit &&
                e.Description == "Cash sale"),
            Arg.Is<EntryCreated>(ev =>
                ev.EntryId == result.Value.Id &&
                ev.Amount == 100m &&
                ev.Type == EntryType.Credit &&
                ev.Description == "Cash sale" &&
                ev.OccurredAtUtc == utcNow.UtcDateTime),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOccurredAt_UsesCurrentUtcTime()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        _clock.GetUtcNow().Returns(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment", null,
            CancellationToken.None);

        result.Value.OccurredAtUtc.ShouldBe(utcNow.UtcDateTime);
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitOccurredAt_KeepsProvidedDate()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var occurredAt = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        _clock.GetUtcNow().Returns(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment",
            occurredAt, CancellationToken.None);

        result.Value.OccurredAtUtc.ShouldBe(occurredAt);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidDomainInput_ReturnsFailureWithoutPersisting()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        _clock.GetUtcNow().Returns(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(0m, EntryType.Credit, "Cash sale", null,
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
        await _entryStore.DidNotReceiveWithAnyArgs().SaveWithOutboxAsync(
            default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WithFutureOccurredAt_ReturnsFailureWithCode()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        _clock.GetUtcNow().Returns(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(10m, EntryType.Credit, "Cash sale",
            utcNow.UtcDateTime.AddDays(1), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OCCURRED_AT_IN_FUTURE");
    }
}
```

### tests/Verity.CashFlow.UnitTests/Application/GetEntryByIdUseCaseTests.cs

```csharp
using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;
using Xunit;

namespace Verity.CashFlow.UnitTests.Application;

public class GetEntryByIdUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();

    [Fact]
    public async Task ExecuteAsync_WhenEntryExists_ReturnsSuccessWithEntry()
    {
        var entry = Entry.Create(100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Value;
        _entryStore.GetByIdAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var sut = new GetEntryByIdUseCase(_entryStore);

        var result = await sut.ExecuteAsync(entry.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entry);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEntryDoesNotExist_ReturnsFailureWithCode()
    {
        _entryStore.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Entry?)null);
        var sut = new GetEntryByIdUseCase(_entryStore);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ENTRY_NOT_FOUND");
    }
}
```

### tests/Verity.CashFlow.UnitTests/Application/ListEntriesByDateUseCaseTests.cs

```csharp
using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;
using Xunit;

namespace Verity.CashFlow.UnitTests.Application;

public class ListEntriesByDateUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();

    [Fact]
    public async Task ExecuteAsync_DelegatesDateAndPagingToStore()
    {
        var date = new DateOnly(2026, 1, 15);
        var page = 2;
        var pageSize = 25;
        var entries = new List<Entry>
        {
            Entry.Create(10m, EntryType.Credit, "Cash sale",
                new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Value
        };
        _entryStore.ListByDateAsync(date, page, pageSize, Arg.Any<CancellationToken>())
            .Returns(entries);
        var sut = new ListEntriesByDateUseCase(_entryStore);

        var result = await sut.ExecuteAsync(date, page, pageSize, CancellationToken.None);

        result.ShouldBe(entries);
        await _entryStore.Received(1).ListByDateAsync(date, page, pageSize,
            Arg.Any<CancellationToken>());
    }
}
```

### Contratos e stubs (compilação)

**src/Verity.CashFlow.Application/IntegrationEvents/EntryCreated.cs**
```csharp
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record EntryCreated(
    Guid EntryId,
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime OccurredAtUtc);
```

**src/Verity.CashFlow.Application/IntegrationEvents/EventTypes.cs**
```csharp
namespace Verity.CashFlow.Application.IntegrationEvents;

public static class EventTypes
{
    public const string EntryCreated = "EntryCreated";
}
```

**src/Verity.CashFlow.Application/Entries/IEntryStore.cs**
```csharp
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.Entries;

public interface IEntryStore
{
    Task SaveWithOutboxAsync(Entry entry, EntryCreated @event, CancellationToken cancellationToken);

    Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Entry>> ListByDateAsync(DateOnly date, int page, int pageSize,
        CancellationToken cancellationToken);
}
```

**Stubs** — os 3 casos de uso com `ExecuteAsync` lançando `NotImplementedException`
(assinaturas idênticas às GREEN abaixo).

## Fase GREEN — implementação

**src/Verity.CashFlow.Application/Entries/CreateEntryUseCase.cs**
```csharp
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Application.Entries;

public sealed class CreateEntryUseCase(IEntryStore entryStore, TimeProvider clock)
{
    public async Task<Result<Entry>> ExecuteAsync(
        decimal amount,
        EntryType type,
        string description,
        DateTime? occurredAtUtc,
        CancellationToken cancellationToken)
    {
        var utcNow = clock.GetUtcNow().UtcDateTime;

        var entryResult = Entry.Create(amount, type, description, occurredAtUtc ?? utcNow,
            utcNow);

        if (entryResult.IsFailure)
            return Result.Failure<Entry>(entryResult.Error);

        var entry = entryResult.Value;

        var @event = new EntryCreated(
            entry.Id,
            entry.Amount,
            entry.Type,
            entry.Description,
            entry.OccurredAtUtc);

        await entryStore.SaveWithOutboxAsync(entry, @event, cancellationToken);

        return Result.Success(entry);
    }
}
```

**src/Verity.CashFlow.Application/Entries/GetEntryByIdUseCase.cs**
```csharp
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Application.Entries;

public sealed class GetEntryByIdUseCase(IEntryStore entryStore)
{
    public async Task<Result<Entry>> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await entryStore.GetByIdAsync(id, cancellationToken);

        return entry is null
            ? Result.Failure<Entry>(EntryErrors.NotFound)
            : Result.Success(entry);
    }
}
```

**src/Verity.CashFlow.Application/Entries/ListEntriesByDateUseCase.cs**
```csharp
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.Entries;

public sealed class ListEntriesByDateUseCase(IEntryStore entryStore)
{
    public Task<IReadOnlyList<Entry>> ExecuteAsync(DateOnly date, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        return entryStore.ListByDateAsync(date, page, pageSize, cancellationToken);
    }
}
```

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
```

## Critérios de aceite

1. Todos os testes das ondas 2 e 3 verdes (8 novos).
2. Casos de uso não referenciam nada de infraestrutura (regra SOLID 1).
3. `CreateEntryUseCase` persiste entrada + evento em **uma** chamada atômica ao port e
   propaga falhas de domínio sem persistir.
4. `GetEntryByIdUseCase` retorna `ENTRY_NOT_FOUND` tipado (sem null no fluxo).
5. `TimeProvider` injetado (determinismo e testabilidade).

## Notas / riscos

- Sem `IUnitOfWork` explícito: a atomicidade entrada+outbox é um requisito de negócio
  expresso como única operação do port `IEntryStore` (ISP + simplicidade). Justificativa
  completa no ADR-0005.
- Validação de paginação (`page`, `pageSize`) fica no endpoint (onda 7), não no caso de
  uso — erro de entrada HTTP, não de domínio.
- `ListEntriesByDateUseCase` retorna a lista diretamente (sem Result): não há falha
  esperada possível.
- Casos de uso são stateless: registrados como transient no DI (onda 7).
