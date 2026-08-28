using Dapper;
using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DapperOutboxStore(SqlConnectionFactory connectionFactory) : IOutboxStore
{
    private const string SelectPendingSql = """
        SELECT TOP (@BatchSize) id, type, payload
        FROM dbo.outbox_messages
        WHERE processed_at_utc IS NULL
        ORDER BY occurred_at_utc;
        """;

    private const string MarkProcessedSql = """
        UPDATE dbo.outbox_messages
        SET processed_at_utc = SYSUTCDATETIME()
        WHERE id = @Id;
        """;

    public async Task<IReadOnlyList<PendingOutboxMessage>> GetPendingAsync(int batchSize,
        CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        var rows = await connection.QueryAsync<PendingOutboxRow>(new CommandDefinition(
            SelectPendingSql, new { BatchSize = batchSize },
            cancellationToken: cancellationToken));

        return rows.Select(row => new PendingOutboxMessage(row.Id, row.Type, row.Payload))
            .ToList();
    }

    public async Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            MarkProcessedSql, new { Id = id }, cancellationToken: cancellationToken));
    }

    private sealed record PendingOutboxRow(Guid Id, string Type, string Payload);
}
