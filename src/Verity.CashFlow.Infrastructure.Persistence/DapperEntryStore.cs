using System.Text.Json;
using System.Text.Json.Serialization;
using Dapper;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DapperEntryStore(SqlConnectionFactory connectionFactory) : IEntryStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private const string InsertEntrySql = """
        INSERT INTO dbo.entries (id, amount, type, description, occurred_at_utc, created_at_utc)
        VALUES (@Id, @Amount, @Type, @Description, @OccurredAtUtc, @CreatedAtUtc);
        """;

    private const string InsertOutboxSql = """
        INSERT INTO dbo.outbox_messages (id, type, payload, occurred_at_utc)
        VALUES (@Id, @Type, @Payload, @OccurredAtUtc);
        """;

    private const string SelectByIdSql = """
        SELECT id, amount, type, description,
               occurred_at_utc AS OccurredAtUtc, created_at_utc AS CreatedAtUtc
        FROM dbo.entries
        WHERE id = @Id;
        """;

    private const string SelectByDateSql = """
        SELECT id, amount, type, description,
               occurred_at_utc AS OccurredAtUtc, created_at_utc AS CreatedAtUtc
        FROM dbo.entries
        WHERE occurred_at_utc >= @Start AND occurred_at_utc < @End
        ORDER BY occurred_at_utc
        OFFSET @Offset ROWS FETCH NEXT @PageSize ROWS ONLY;
        """;

    public async Task SaveWithOutboxAsync(Entry entry, EntryCreated @event,
        CancellationToken cancellationToken)
    {
        await using SqlConnection connection = await connectionFactory
            .OpenAsync(cancellationToken);
        await using var transaction = await connection
            .BeginTransactionAsync(cancellationToken);

        await connection.ExecuteAsync(new CommandDefinition(
            InsertEntrySql,
            new
            {
                entry.Id,
                entry.Amount,
                Type = (byte)entry.Type,
                entry.Description,
                entry.OccurredAtUtc,
                entry.CreatedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

        var payload = JsonSerializer.Serialize(@event, SerializerOptions);

        await connection.ExecuteAsync(new CommandDefinition(
            InsertOutboxSql,
            new
            {
                Id = Guid.NewGuid(),
                Type = EventTypes.EntryCreated,
                Payload = payload,
                OccurredAtUtc = entry.CreatedAtUtc
            },
            transaction,
            cancellationToken: cancellationToken));

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        var row = await connection.QuerySingleOrDefaultAsync<EntryRow>(
            new CommandDefinition(SelectByIdSql, new { Id = id },
                cancellationToken: cancellationToken));

        return row?.ToDomain();
    }

    public async Task<IReadOnlyList<Entry>> ListByDateAsync(DateOnly date, int page,
        int pageSize, CancellationToken cancellationToken)
    {
        await using var connection = await connectionFactory
            .OpenAsync(cancellationToken);

        var start = date.ToDateTime(TimeOnly.MinValue);
        var end = start.AddDays(1);

        var rows = await connection.QueryAsync<EntryRow>(new CommandDefinition(
            SelectByDateSql,
            new { Start = start, End = end, Offset = (page - 1) * pageSize, PageSize = pageSize },
            cancellationToken: cancellationToken));

        return rows.Select(row => row.ToDomain()).ToList();
    }

    private sealed record EntryRow(
        Guid Id,
        decimal Amount,
        byte Type,
        string Description,
        DateTime OccurredAtUtc,
        DateTime CreatedAtUtc)
    {
        public Entry ToDomain() =>
            Entry.Restore(Id, Amount, (EntryType)Type, Description, OccurredAtUtc, CreatedAtUtc);
    }
}
