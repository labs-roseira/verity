using Dapper;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DapperEntryProjection(SqlConnectionFactory connectionFactory)
    : IEntryProjection
{
    private const string ExistsSql = """
        SELECT CAST(CASE WHEN EXISTS (
            SELECT 1 FROM dbo.processed_entries WHERE entry_id = @EntryId
        ) THEN 1 ELSE 0 END AS BIT);
        """;

    private const string InsertProcessedSql = """
        INSERT INTO dbo.processed_entries (entry_id, processed_at_utc)
        VALUES (@EntryId, SYSUTCDATETIME());
        """;

    private const string UpsertBalanceSql = """
        MERGE INTO dbo.daily_balances AS target
        USING (SELECT @Date AS [date]) AS source
        ON target.[date] = source.[date]
        WHEN MATCHED THEN
            UPDATE SET
                total_credits  = target.total_credits + @CreditDelta,
                total_debits   = target.total_debits  + @DebitDelta,
                updated_at_utc = SYSUTCDATETIME()
        WHEN NOT MATCHED THEN
            INSERT ([date], total_credits, total_debits, updated_at_utc)
            VALUES (source.[date], @CreditDelta, @DebitDelta, SYSUTCDATETIME());
        """;

    public async Task<bool> ApplyAsync(EntryCreated @event,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await connectionFactory
            .OpenAsync(cancellationToken);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken);

        var alreadyProcessed = await connection.ExecuteScalarAsync<bool>(
            new CommandDefinition(ExistsSql, new { @event.EntryId }, transaction,
                cancellationToken: cancellationToken));

        if (alreadyProcessed)
            return false;

        var date = DateOnly.FromDateTime(@event.OccurredAtUtc);
        var creditDelta = @event.Type == EntryType.Credit ? @event.Amount : 0m;
        var debitDelta = @event.Type == EntryType.Debit ? @event.Amount : 0m;

        await connection.ExecuteAsync(new CommandDefinition(InsertProcessedSql,
            new { @event.EntryId }, transaction, cancellationToken: cancellationToken));

        await connection.ExecuteAsync(new CommandDefinition(UpsertBalanceSql,
            new { Date = date.ToDateTime(TimeOnly.MinValue), CreditDelta = creditDelta, DebitDelta = debitDelta },
            transaction, cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
