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
            new CommandDefinition(SelectByDateSql, new { Date = date.ToDateTime(TimeOnly.MinValue) },
                cancellationToken: cancellationToken));

        return row is null
            ? null
            : new DailyBalanceSnapshot(DateOnly.FromDateTime(row.Date), row.TotalCredits, row.TotalDebits);
    }

    public async Task<decimal> GetAccumulatedBalanceAsync(DateOnly upToDate,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        return await connection.ExecuteScalarAsync<decimal>(new CommandDefinition(
            AccumulatedBalanceSql, new { UpToDate = upToDate.ToDateTime(TimeOnly.MinValue) },
            cancellationToken: cancellationToken));
    }

    private sealed record SnapshotRow(DateTime Date, decimal TotalCredits, decimal TotalDebits);
}
