# Wave 6 — Relatório Consolidado (TDD)

## Objetivo

Implementar o caso de uso do relatório diário consolidado: totais de créditos/débitos,
saldo do dia e saldo acumulado, com regra "dia sem dados → zeros" (sucesso, não falha —
por isso o caso de uso retorna o valor diretamente, sem `Result`).

## Pré-requisitos

- Onda 5 concluída.
- `contracts.md` seções 5 (port `IConsolidatedBalanceReader`) e 6.2 lidas.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `tests/Verity.CashFlow.UnitTests/Application/GetDailyConsolidatedBalanceUseCaseTests.cs` | RED |
| `src/Verity.CashFlow.Domain/Consolidation/DailyBalanceSnapshot.cs` | RED |
| `src/Verity.CashFlow.Domain/Consolidation/DailyConsolidatedBalance.cs` | RED |
| `src/Verity.CashFlow.Application/Consolidation/IConsolidatedBalanceReader.cs` | RED |
| `src/Verity.CashFlow.Application/Consolidation/GetDailyConsolidatedBalanceUseCase.cs` | RED (stub) → GREEN |
| `src/Verity.CashFlow.Infrastructure.Persistence/DapperConsolidatedBalanceReader.cs` | GREEN |

## Fase RED — testes + contratos

### tests/Verity.CashFlow.UnitTests/Application/GetDailyConsolidatedBalanceUseCaseTests.cs

```csharp
using NSubstitute;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Domain.Consolidation;
using Xunit;

namespace Verity.CashFlow.UnitTests.Application;

public class GetDailyConsolidatedBalanceUseCaseTests
{
    private readonly IConsolidatedBalanceReader _reader =
        Substitute.For<IConsolidatedBalanceReader>();

    [Fact]
    public async Task ExecuteAsync_WhenDateHasNoData_ReturnsAllZeros()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceSnapshot?)null);
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(0m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.Date.ShouldBe(date);
        result.TotalCredits.ShouldBe(0m);
        result.TotalDebits.ShouldBe(0m);
        result.DayBalance.ShouldBe(0m);
        result.AccumulatedBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDayTotals_ComputesDayBalance()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new DailyBalanceSnapshot(date, 150.75m, 30m));
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(120.75m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.TotalCredits.ShouldBe(150.75m);
        result.TotalDebits.ShouldBe(30m);
        result.DayBalance.ShouldBe(120.75m);
        result.AccumulatedBalance.ShouldBe(120.75m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDebitsGreaterThanCredits_ReturnsNegativeDayBalance()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new DailyBalanceSnapshot(date, 50m, 80m));
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(-30m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.DayBalance.ShouldBe(-30m);
        result.AccumulatedBalance.ShouldBe(-30m);
    }

    [Fact]
    public async Task ExecuteAsync_QueriesAccumulatedUpToRequestedDate()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceSnapshot?)null);
        _reader.GetAccumulatedBalanceAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(0m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        await sut.ExecuteAsync(date, CancellationToken.None);

        await _reader.Received(1).GetAccumulatedBalanceAsync(date,
            Arg.Any<CancellationToken>());
    }
}
```

### Modelos, port e stub (compilação)

**src/Verity.CashFlow.Domain/Consolidation/DailyBalanceSnapshot.cs**
```csharp
namespace Verity.CashFlow.Domain.Consolidation;

public sealed record DailyBalanceSnapshot(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits);
```

**src/Verity.CashFlow.Domain/Consolidation/DailyConsolidatedBalance.cs**
```csharp
namespace Verity.CashFlow.Domain.Consolidation;

public sealed record DailyConsolidatedBalance(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal DayBalance,
    decimal AccumulatedBalance);
```

**src/Verity.CashFlow.Application/Consolidation/IConsolidatedBalanceReader.cs**
```csharp
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Application.Consolidation;

public interface IConsolidatedBalanceReader
{
    Task<DailyBalanceSnapshot?> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken);

    Task<decimal> GetAccumulatedBalanceAsync(DateOnly upToDate,
        CancellationToken cancellationToken);
}
```

**src/Verity.CashFlow.Application/Consolidation/GetDailyConsolidatedBalanceUseCase.cs** (stub)
```csharp
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Application.Consolidation;

public sealed class GetDailyConsolidatedBalanceUseCase(
    IConsolidatedBalanceReader reader)
{
    public Task<DailyConsolidatedBalance> ExecuteAsync(DateOnly date,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
```

## Fase GREEN — implementação

**src/Verity.CashFlow.Application/Consolidation/GetDailyConsolidatedBalanceUseCase.cs**
```csharp
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Application.Consolidation;

public sealed class GetDailyConsolidatedBalanceUseCase(
    IConsolidatedBalanceReader reader)
{
    public async Task<DailyConsolidatedBalance> ExecuteAsync(DateOnly date,
        CancellationToken cancellationToken)
    {
        var snapshot = await reader.GetByDateAsync(date, cancellationToken);

        var accumulated = await reader.GetAccumulatedBalanceAsync(date, cancellationToken);

        var totalCredits = snapshot?.TotalCredits ?? 0m;
        var totalDebits = snapshot?.TotalDebits ?? 0m;

        return new DailyConsolidatedBalance(
            date,
            totalCredits,
            totalDebits,
            totalCredits - totalDebits,
            accumulated);
    }
}
```

**src/Verity.CashFlow.Infrastructure.Persistence/DapperConsolidatedBalanceReader.cs**
```csharp
using Dapper;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DapperConsolidatedBalanceReader(
    SqlConnectionFactory connectionFactory) : IConsolidatedBalanceReader
{
    private const string SelectByDateSql = """
        SELECT [date] AS Date, total_credits AS TotalCredits, total_debits AS TotalDebits
        FROM dbo.daily_balances
        WHERE [date] = @Date;
        """;

    private const string AccumulatedBalanceSql = """
        SELECT COALESCE(SUM(total_credits - total_debits), 0)
        FROM dbo.daily_balances
        WHERE [date] <= @UpToDate;
        """;

    public async Task<DailyBalanceSnapshot?> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<SnapshotRow>(
            new CommandDefinition(SelectByDateSql, new { Date = date },
                cancellationToken: cancellationToken));

        return row is null
            ? null
            : new DailyBalanceSnapshot(row.Date, row.TotalCredits, row.TotalDebits);
    }

    public async Task<decimal> GetAccumulatedBalanceAsync(DateOnly upToDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            AccumulatedBalanceSql, new { UpToDate = upToDate },
            cancellationToken: cancellationToken));
    }

    private sealed record SnapshotRow(DateOnly Date, decimal TotalCredits, decimal TotalDebits);
}
```

## Comandos de verificação

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
```

## Critérios de aceite

1. 4 testes do caso de uso verdes (mais ondas anteriores).
2. Dia sem dados → zeros (200 na API, nunca 404) — sem `Result` (não é falha).
3. `DayBalance = TotalCredits - TotalDebits`; `AccumulatedBalance` delegado ao reader.
4. Reader Dapper com 2 queries simples (sem agregação em memória).

## Notas / riscos

- As 2 queries do reader poderiam ser 1 (CTE/JOIN) — mantidas separadas por clareza;
  volume do desafio (50 req/s de pico é trivial para MSSQL) não justifica otimização.
- Saldo acumulado usa `daily_balances` (projeção), não `entries` — a Consolidation não
  lê tabelas do módulo Entries (isolamento lógico no banco único).
- `DateOnly` no Dapper: suportado nativamente pelo `Microsoft.Data.SqlClient` atual —
  se houver problema de mapeamento, serializar como string e converter (validar na onda 7).
