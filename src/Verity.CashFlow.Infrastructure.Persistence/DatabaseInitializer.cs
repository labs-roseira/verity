using Dapper;
using Microsoft.Data.SqlClient;

namespace Verity.CashFlow.Infrastructure.Persistence;

public sealed class DatabaseInitializer(string connectionString, string databaseName)
{
    private const string SchemaSql = """
        IF OBJECT_ID(N'dbo.entries') IS NULL
        BEGIN
            CREATE TABLE dbo.entries
            (
                id              UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_entries PRIMARY KEY,
                amount          DECIMAL(18, 2)   NOT NULL,
                type            TINYINT          NOT NULL,
                description     NVARCHAR(500)    NOT NULL,
                occurred_at_utc DATETIME2        NOT NULL,
                created_at_utc  DATETIME2        NOT NULL
            );

            CREATE INDEX IX_entries_occurred_at_utc ON dbo.entries (occurred_at_utc);
        END;

        IF OBJECT_ID(N'dbo.outbox_messages') IS NULL
        BEGIN
            CREATE TABLE dbo.outbox_messages
            (
                id               UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_outbox_messages PRIMARY KEY,
                type             NVARCHAR(200)    NOT NULL,
                payload          NVARCHAR(MAX)    NOT NULL,
                occurred_at_utc  DATETIME2        NOT NULL,
                processed_at_utc DATETIME2        NULL
            );

            CREATE INDEX IX_outbox_messages_pending
                ON dbo.outbox_messages (occurred_at_utc)
                WHERE processed_at_utc IS NULL;
        END;

        IF OBJECT_ID(N'dbo.processed_entries') IS NULL
        BEGIN
            CREATE TABLE dbo.processed_entries
            (
                entry_id          UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_processed_entries PRIMARY KEY,
                processed_at_utc  DATETIME2        NOT NULL
            );
        END;

        IF OBJECT_ID(N'dbo.daily_balances') IS NULL
        BEGIN
            CREATE TABLE dbo.daily_balances
            (
                date            DATE           NOT NULL CONSTRAINT PK_daily_balances PRIMARY KEY,
                total_credits   DECIMAL(18, 2) NOT NULL,
                total_debits    DECIMAL(18, 2) NOT NULL,
                updated_at_utc  DATETIME2      NOT NULL
            );
        END;
        """;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        var masterBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = "master"
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using (var master = new SqlConnection(masterBuilder.ConnectionString))
                {
                    await master.OpenAsync(cancellationToken);
                    await master.ExecuteAsync(new CommandDefinition(
                        $"IF DB_ID(N'{databaseName}') IS NULL CREATE DATABASE [{databaseName}]",
                        cancellationToken: cancellationToken));
                }
                break;
            }
            catch (SqlException ex) when (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }

        await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);

        var databaseBuilder = new SqlConnectionStringBuilder(connectionString)
        {
            InitialCatalog = databaseName
        };

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await using var database = new SqlConnection(databaseBuilder.ConnectionString);
                await database.OpenAsync(cancellationToken);
                await database.ExecuteAsync(new CommandDefinition(
                    SchemaSql, cancellationToken: cancellationToken));
                return;
            }
            catch (SqlException ex) when (ex.Number == 2714 && attempt < 5)
            {
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
        }
    }
}
