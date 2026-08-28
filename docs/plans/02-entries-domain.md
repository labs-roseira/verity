# Wave 2 — Domínio: Results + Entries (TDD)

## Objetivo

Criar o primitivo `Result`/`Result<T>`/`Error` e o domínio de lançamentos com validação
via factory method que **retorna `Result<Entry>`** (falhas esperadas sem exceções),
via TDD: `Entry`, `EntryType`, `EntryErrors`.

## Pré-requisitos

- Onda 1 concluída (projetos criados e compilando).
- `contracts.md` seções 2 (contratos de código) e convenções seção 7 (padrão Result) lidas.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `tests/Verity.CashFlow.UnitTests/Domain/ResultTests.cs` | RED |
| `tests/Verity.CashFlow.UnitTests/Domain/EntryTests.cs` | RED |
| `src/Verity.CashFlow.Domain/Results/Error.cs` | RED |
| `src/Verity.CashFlow.Domain/Results/Result.cs` | RED |
| `src/Verity.CashFlow.Domain/Entries/EntryType.cs` | RED |
| `src/Verity.CashFlow.Domain/Entries/EntryErrors.cs` | RED |
| `src/Verity.CashFlow.Domain/Entries/Entry.cs` | RED (stub) → GREEN |

## Fase RED — testes + primitivos

> `Error` e `Result` são primitivos do padrão (não há lógica condicional para
> "implementar depois"): entram completos já na fase RED. O stub é apenas `Entry`.

### tests/Verity.CashFlow.UnitTests/Domain/ResultTests.cs

```csharp
using Verity.CashFlow.Domain.Results;
using Xunit;

namespace Verity.CashFlow.UnitTests.Domain;

public class ResultTests
{
    [Fact]
    public void Success_IsSuccessTrueAndErrorNone()
    {
        var result = Result.Success();

        result.IsSuccess.ShouldBeTrue();
        result.IsFailure.ShouldBeFalse();
        result.Error.ShouldBe(Error.None);
    }

    [Fact]
    public void Failure_IsFailureTrueAndCarriesError()
    {
        var error = new Error("SOME_CODE", "Something went wrong.");

        var result = Result.Failure(error);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("SOME_CODE");
        result.Error.Message.ShouldBe("Something went wrong.");
    }

    [Fact]
    public void SuccessOfT_ExposesValue()
    {
        var result = Result.Success(42);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(42);
    }

    [Fact]
    public void FailureOfT_ValueAccessThrows()
    {
        var result = Result.Failure<int>(new Error("SOME_CODE", "Failed."));

        Should.Throw<InvalidOperationException>(() => _ = result.Value);
    }
}
```

### tests/Verity.CashFlow.UnitTests/Domain/EntryTests.cs

```csharp
using Verity.CashFlow.Domain.Entries;
using Xunit;

namespace Verity.CashFlow.UnitTests.Domain;

public class EntryTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidCredit_ReturnsSuccessWithEntry()
    {
        var occurredAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        var result = Entry.Create(100.50m, EntryType.Credit, "Cash sale", occurredAt, UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldNotBe(Guid.Empty);
        result.Value.Amount.ShouldBe(100.50m);
        result.Value.Type.ShouldBe(EntryType.Credit);
        result.Value.Description.ShouldBe("Cash sale");
        result.Value.OccurredAtUtc.ShouldBe(occurredAt);
        result.Value.CreatedAtUtc.ShouldBe(UtcNow);
    }

    [Fact]
    public void Create_WithValidDebit_ReturnsSuccessWithDebitType()
    {
        var result = Entry.Create(40m, EntryType.Debit, "Supplier payment",
            UtcNow.AddHours(-1), UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Type.ShouldBe(EntryType.Debit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WithNonPositiveAmount_ReturnsFailureWithCode(decimal amount)
    {
        var result = Entry.Create(amount, EntryType.Credit, "Cash sale",
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
    }

    [Fact]
    public void Create_WithUndefinedType_ReturnsFailureWithCode()
    {
        var result = Entry.Create(100m, (EntryType)99, "Cash sale",
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ENTRY_TYPE_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingDescription_ReturnsFailureWithCode(string? description)
    {
        var result = Entry.Create(100m, EntryType.Debit, description!,
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DESCRIPTION_REQUIRED");
    }

    [Fact]
    public void Create_WithDescriptionLongerThanMaxLength_ReturnsFailureWithCode()
    {
        var description = new string('a', 501);

        var result = Entry.Create(100m, EntryType.Debit, description,
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DESCRIPTION_TOO_LONG");
    }

    [Fact]
    public void Create_WithDescriptionAtMaxLength_ReturnsSuccess()
    {
        var description = new string('a', 500);

        var result = Entry.Create(100m, EntryType.Credit, description,
            UtcNow.AddHours(-1), UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.Length.ShouldBe(500);
    }

    [Fact]
    public void Create_WithFutureOccurrenceDate_ReturnsFailureWithCode()
    {
        var result = Entry.Create(100m, EntryType.Credit, "Cash sale",
            UtcNow.AddMinutes(1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OCCURRED_AT_IN_FUTURE");
    }

    [Fact]
    public void Create_WithOccurrenceExactlyNow_ReturnsSuccess()
    {
        var result = Entry.Create(100m, EntryType.Credit, "Cash sale", UtcNow, UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccurredAtUtc.ShouldBe(UtcNow);
    }

    [Fact]
    public void Create_WithSurroundingWhitespace_TrimsDescription()
    {
        var result = Entry.Create(100m, EntryType.Credit, "  Cash sale  ",
            UtcNow.AddHours(-1), UtcNow);

        result.Value.Description.ShouldBe("Cash sale");
    }

    [Fact]
    public void Restore_WithPersistedValues_ReturnsEntryWithSameIdentity()
    {
        var id = Guid.NewGuid();

        var entry = Entry.Restore(id, 50m, EntryType.Debit, "Supplier payment",
            UtcNow.AddHours(-2), UtcNow.AddHours(-1));

        entry.Id.ShouldBe(id);
        entry.Amount.ShouldBe(50m);
        entry.Type.ShouldBe(EntryType.Debit);
        entry.Description.ShouldBe("Supplier payment");
    }
}
```

> **Atenção:** os arquivos de teste acima usam Shouldly — garanta `using Shouldly;`
> via `GlobalUsings.cs` do projeto de teste:
>
> ```csharp
> global using Shouldly;
> global using Xunit;
> ```
>
> (substitui o `global using Xunit;` criado pelo template).

### Primitivos e stub (compilação)

**src/Verity.CashFlow.Domain/Results/Error.cs**
```csharp
namespace Verity.CashFlow.Domain.Results;

public sealed record Error(string Code, string Message)
{
    public static readonly Error None = new(string.Empty, string.Empty);
}
```

**src/Verity.CashFlow.Domain/Results/Result.cs**
```csharp
namespace Verity.CashFlow.Domain.Results;

public sealed class Result
{
    protected Result(bool isSuccess, Error error)
    {
        IsSuccess = isSuccess;
        Error = error;
    }

    public bool IsSuccess { get; }

    public bool IsFailure => !IsSuccess;

    public Error Error { get; }

    public static Result Success() => new(true, Error.None);

    public static Result Failure(Error error) => new(false, error);

    public static Result<T> Success<T>(T value) => new(value, true, Error.None);

    public static Result<T> Failure<T>(Error error) => new(default, false, error);
}

public sealed class Result<T>(T? value, bool isSuccess, Error error)
    : Result(isSuccess, error)
{
    private readonly T? _value = value;

    public T Value => IsSuccess
        ? _value!
        : throw new InvalidOperationException(
            "The value of a failed result cannot be accessed.");
}
```

**src/Verity.CashFlow.Domain/Entries/EntryType.cs**
```csharp
namespace Verity.CashFlow.Domain.Entries;

public enum EntryType
{
    Credit = 1,
    Debit = 2
}
```

**src/Verity.CashFlow.Domain/Entries/EntryErrors.cs**
```csharp
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Domain.Entries;

public static class EntryErrors
{
    public static Error AmountMustBePositive { get; } =
        new("AMOUNT_MUST_BE_POSITIVE", "Entry amount must be greater than zero.");

    public static Error TypeInvalid { get; } =
        new("ENTRY_TYPE_INVALID", "Entry type must be Credit or Debit.");

    public static Error DescriptionRequired { get; } =
        new("DESCRIPTION_REQUIRED", "Entry description is required.");

    public static Error DescriptionTooLong { get; } =
        new("DESCRIPTION_TOO_LONG", "Entry description must have at most 500 characters.");

    public static Error OccurredAtInFuture { get; } =
        new("OCCURRED_AT_IN_FUTURE", "Entry occurrence date cannot be in the future.");

    public static Error NotFound { get; } =
        new("ENTRY_NOT_FOUND", "Entry was not found.");
}
```

**src/Verity.CashFlow.Domain/Entries/Entry.cs** (stub)
```csharp
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Domain.Entries;

public sealed class Entry
{
    public static Result<Entry> Create(decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime utcNow) =>
        throw new NotImplementedException();

    public static Entry Restore(Guid id, decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime createdAtUtc) =>
        throw new NotImplementedException();
}
```

## Fase GREEN — implementação

**src/Verity.CashFlow.Domain/Entries/Entry.cs**
```csharp
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Domain.Entries;

public sealed class Entry
{
    public const int DescriptionMaxLength = 500;

    private Entry(Guid id, decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime createdAtUtc)
    {
        Id = id;
        Amount = amount;
        Type = type;
        Description = description;
        OccurredAtUtc = occurredAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public decimal Amount { get; }

    public EntryType Type { get; }

    public string Description { get; }

    public DateTime OccurredAtUtc { get; }

    public DateTime CreatedAtUtc { get; }

    public static Result<Entry> Create(decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime utcNow)
    {
        if (amount <= 0)
            return Result.Failure<Entry>(EntryErrors.AmountMustBePositive);

        if (!Enum.IsDefined(type))
            return Result.Failure<Entry>(EntryErrors.TypeInvalid);

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<Entry>(EntryErrors.DescriptionRequired);

        if (description.Length > DescriptionMaxLength)
            return Result.Failure<Entry>(EntryErrors.DescriptionTooLong);

        if (occurredAtUtc > utcNow)
            return Result.Failure<Entry>(EntryErrors.OccurredAtInFuture);

        return Result.Success(new Entry(Guid.NewGuid(), amount, type, description.Trim(),
            occurredAtUtc, utcNow));
    }

    public static Entry Restore(Guid id, decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime createdAtUtc)
    {
        return new Entry(id, amount, type, description, occurredAtUtc, createdAtUtc);
    }
}
```

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
```

## Critérios de aceite

1. 15 testes verdes (4 de `Result` + 11 de `Entry`).
2. `Entry` imutável (sem setters, construtor privado, factory methods).
3. Falhas esperadas retornam `Result<Entry>` com `Error.Code` — nenhuma exceção de domínio.
4. `Restore` não valida (rehidratação de dados já validados).
5. Nenhum comentário no código; mensagens de erro em inglês.

## Notas / riscos

- `utcNow` entra como parâmetro em `Create` para determinismo (SRP: entidade valida,
  não mede tempo — quem mede é o caso de uso com `TimeProvider`).
- `EntryErrors` centraliza os erros do módulo — usado pelo domínio (validações) e pela
  Application (`NotFound`), e consultado pelos endpoints para mapear código → HTTP.
- `Result` e `Result<T>` no mesmo arquivo: são um primitivo único (exceção documentada
  à regra "1 classe por arquivo" — 60 linhas coesas).
- Convenção de datas: sempre UTC; conversão de fuso é responsabilidade do cliente da API.
