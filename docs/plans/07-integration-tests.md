# Wave 7 — Stores Dapper, Endpoints e Testes de Integração/E2E

## Objetivo

Completar a persistência da Entries (`DapperEntryStore`, `DapperOutboxStore`), expor os
endpoints HTTP das duas APIs (composição DI + OpenAPI descritivo + **pattern match de
`Result`**) e escrever os testes de integração com Testcontainers (MSSQL + RabbitMQ
reais): endpoints, e2e completo (`POST → outbox → RabbitMQ → consumer → projeção →
relatório`) e prova de resiliência (POST retorna 201 com broker fora).

**Ordem de escrita dentro da onda:** GREEN parte 1 (infra + endpoints, para compilar) →
RED (testes de integração) → GREEN parte 2 (ajustes até verde). Os testes unitários das
ondas anteriores devem continuar verdes.

## Pré-requisitos

- Ondas 2–6 concluídas.
- Docker Desktop instalado e rodando (testes fazem auto-skip sem ele, mas o objetivo
  desta onda é rodá-los de verdade).
- `contracts.md` completo lido.

## Arquivos a criar

| Arquivo | Fase |
|---|---|
| `src/Verity.CashFlow.Infrastructure.Persistence/DapperEntryStore.cs` | GREEN 1 |
| `src/Verity.CashFlow.Infrastructure.Persistence/DapperOutboxStore.cs` | GREEN 1 |
| `src/Verity.CashFlow.Entries.Api/Endpoints/Contracts.cs` | GREEN 1 |
| `src/Verity.CashFlow.Entries.Api/Endpoints/EntryEndpoints.cs` | GREEN 1 |
| `src/Verity.CashFlow.Entries.Api/Program.cs` (final) | GREEN 1 |
| `src/Verity.CashFlow.Consolidation.Api/Endpoints/Contracts.cs` | GREEN 1 |
| `src/Verity.CashFlow.Consolidation.Api/Endpoints/ConsolidatedBalanceEndpoints.cs` | GREEN 1 |
| `src/Verity.CashFlow.Consolidation.Api/Program.cs` (final) | GREEN 1 |
| `tests/Verity.CashFlow.IntegrationTests/Support/DockerRequiredFact.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Support/CashFlowCollection.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Fixtures/CashFlowContainers.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Fixtures/EntriesApiFactory.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Fixtures/ConsolidationApiFactory.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Entries/CreateEntryEndpointTests.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Entries/GetEntryEndpointsTests.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/Entries/ResilienceTests.cs` | RED |
| `tests/Verity.CashFlow.IntegrationTests/E2E/CashFlowEndToEndTests.cs` | RED |

## Fase GREEN 1 — persistência e endpoints

### src/Verity.CashFlow.Infrastructure.Persistence/DapperEntryStore.cs

```csharp
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
        SELECT id, amount, type, description, occurred_at_utc, created_at_utc
        FROM dbo.entries
        WHERE id = @Id;
        """;

    private const string SelectByDateSql = """
        SELECT id, amount, type, description, occurred_at_utc, created_at_utc
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
```

### src/Verity.CashFlow.Infrastructure.Persistence/DapperOutboxStore.cs

```csharp
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
```

### src/Verity.CashFlow.Entries.Api/Endpoints/Contracts.cs

```csharp
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Entries.Api.Endpoints;

public sealed record CreateEntryRequest(
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime? OccurredAtUtc);

public sealed record EntryResponse(
    Guid Id,
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc)
{
    public static EntryResponse From(Entry entry) =>
        new(entry.Id, entry.Amount, entry.Type, entry.Description,
            entry.OccurredAtUtc, entry.CreatedAtUtc);
}
```

### src/Verity.CashFlow.Entries.Api/Endpoints/EntryEndpoints.cs

```csharp
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;
using Microsoft.AspNetCore.Mvc;

namespace Verity.CashFlow.Entries.Api.Endpoints;

public static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/entries").WithTags("Entries");

        group.MapPost("/", CreateEntry)
            .WithName("CreateEntry")
            .WithSummary("Register a credit or debit cash flow entry.")
            .WithDescription(
                "Persists the entry and stores an integration event for the consolidation "
                + "service. Returns 201 even if the message broker is unavailable.")
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetEntryById)
            .WithName("GetEntryById")
            .WithSummary("Get a single entry by id.")
            .Produces<EntryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListEntriesByDate)
            .WithName("ListEntriesByDate")
            .WithSummary("List entries of a given date (paged).")
            .Produces<IReadOnlyList<EntryResponse>>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> CreateEntry(
        CreateEntryRequest request,
        CreateEntryUseCase createEntry,
        CancellationToken cancellationToken)
    {
        var result = await createEntry.ExecuteAsync(
            request.Amount,
            request.Type,
            request.Description,
            request.OccurredAtUtc,
            cancellationToken);

        return result.IsSuccess
            ? Results.Created($"/api/entries/{result.Value.Id}",
                EntryResponse.From(result.Value))
            : Results.Problem(
                title: "Entry validation failed.",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = result.Error.Code
                });
    }

    private static async Task<IResult> GetEntryById(
        Guid id,
        GetEntryByIdUseCase getEntryById,
        CancellationToken cancellationToken)
    {
        var result = await getEntryById.ExecuteAsync(id, cancellationToken);

        return result.IsSuccess
            ? Results.Ok(EntryResponse.From(result.Value))
            : Results.Problem(
                title: "Entry not found.",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = result.Error.Code
                });
    }

    private static async Task<IResult> ListEntriesByDate(
        DateOnly? date,
        int? page,
        int? pageSize,
        ListEntriesByDateUseCase listEntriesByDate,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (date is null)
            errors["date"] = ["Query parameter 'date' (yyyy-MM-dd) is required."];

        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;

        if (currentPage < 1)
            errors["page"] = ["Page must be at least 1."];

        if (currentPageSize < 1 || currentPageSize > 100)
            errors["pageSize"] = ["Page size must be between 1 and 100."];

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var entries = await listEntriesByDate.ExecuteAsync(
            date!.Value, currentPage, currentPageSize, cancellationToken);

        return Results.Ok(entries.Select(EntryResponse.From).ToList());
    }
}
```

### src/Verity.CashFlow.Entries.Api/Program.cs (final)

```csharp
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Entries.Api.Endpoints;
using Verity.CashFlow.Infrastructure.Messaging;
using Verity.CashFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

builder.Services.AddSingleton(TimeProvider.System);

var connectionString = builder.Configuration.GetConnectionString("CashFlowDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:CashFlowDatabase is not configured.");

var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog
    ?? throw new InvalidOperationException(
        "Connection string must define an initial catalog.");

builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddSingleton(new DatabaseInitializer(connectionString, databaseName));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<IEntryStore, DapperEntryStore>();
builder.Services.AddSingleton<IOutboxStore, DapperOutboxStore>();
builder.Services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

builder.Services.AddTransient<CreateEntryUseCase>();
builder.Services.AddTransient<GetEntryByIdUseCase>();
builder.Services.AddTransient<ListEntriesByDateUseCase>();

builder.Services.AddHostedService<OutboxDispatcher>();

var app = builder.Build();

var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
await initializer.InitializeAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapEntryEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "Ok" }))
    .WithSummary("Liveness probe.")
    .ExcludeFromDescription();

app.Run();

public partial class Program;
```

### src/Verity.CashFlow.Consolidation.Api/Endpoints/Contracts.cs

```csharp
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Consolidation.Api.Endpoints;

public sealed record ConsolidatedBalanceResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal DayBalance,
    decimal AccumulatedBalance)
{
    public static ConsolidatedBalanceResponse From(DailyConsolidatedBalance balance) =>
        new(balance.Date, balance.TotalCredits, balance.TotalDebits,
            balance.DayBalance, balance.AccumulatedBalance);
}
```

### src/Verity.CashFlow.Consolidation.Api/Endpoints/ConsolidatedBalanceEndpoints.cs

```csharp
using Verity.CashFlow.Application.Consolidation;

namespace Verity.CashFlow.Consolidation.Api.Endpoints;

public static class ConsolidatedBalanceEndpoints
{
    public static IEndpointRouteBuilder MapConsolidatedBalanceEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/consolidated").WithTags("Consolidation");

        group.MapGet("/{date}", GetDailyConsolidatedBalance)
            .WithName("GetDailyConsolidatedBalance")
            .WithSummary("Get the consolidated daily balance for a given date.")
            .WithDescription(
                "Returns total credits, total debits, the day balance and the accumulated "
                + "balance up to the requested date. Dates without entries return zeros.")
            .Produces<ConsolidatedBalanceResponse>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetDailyConsolidatedBalance(
        DateOnly date,
        GetDailyConsolidatedBalanceUseCase getDailyConsolidatedBalance,
        CancellationToken cancellationToken)
    {
        var balance = await getDailyConsolidatedBalance.ExecuteAsync(
            date, cancellationToken);

        return Results.Ok(ConsolidatedBalanceResponse.From(balance));
    }
}
```

### src/Verity.CashFlow.Consolidation.Api/Program.cs (final)

```csharp
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Consolidation.Api.Endpoints;
using Verity.CashFlow.Infrastructure.Messaging;
using Verity.CashFlow.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();

builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

var connectionString = builder.Configuration.GetConnectionString("CashFlowDatabase")
    ?? throw new InvalidOperationException(
        "ConnectionStrings:CashFlowDatabase is not configured.");

var databaseName = new SqlConnectionStringBuilder(connectionString).InitialCatalog
    ?? throw new InvalidOperationException(
        "Connection string must define an initial catalog.");

builder.Services.AddSingleton(new SqlConnectionFactory(connectionString));
builder.Services.AddSingleton(new DatabaseInitializer(connectionString, databaseName));

builder.Services.Configure<RabbitMqOptions>(
    builder.Configuration.GetSection(RabbitMqOptions.SectionName));

builder.Services.AddSingleton<IEntryProjection, DapperEntryProjection>();
builder.Services.AddSingleton<IConsolidatedBalanceReader, DapperConsolidatedBalanceReader>();

builder.Services.AddSingleton(serviceProvider =>
{
    var projection = serviceProvider.GetRequiredService<IEntryProjection>();
    var logger = serviceProvider.GetRequiredService<ILogger<EntryCreatedProcessor>>();
    return new EntryCreatedProcessor(projection, logger);
});

builder.Services.AddTransient<GetDailyConsolidatedBalanceUseCase>();

builder.Services.AddHostedService<EntryCreatedConsumer>();

var app = builder.Build();

var initializer = app.Services.GetRequiredService<DatabaseInitializer>();
await initializer.InitializeAsync(app.Lifetime.ApplicationStopping);

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.MapConsolidatedBalanceEndpoints();

app.MapGet("/health", () => Results.Ok(new { status = "Ok" }))
    .WithSummary("Liveness probe.")
    .ExcludeFromDescription();

app.Run();

public partial class Program;
```

## Fase RED — testes de integração

### tests/Verity.CashFlow.IntegrationTests/Support/DockerRequiredFact.cs

```csharp
using System.Diagnostics;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Support;

public sealed class DockerRequiredFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> DockerAvailable = new(CheckDocker);

    public DockerRequiredFactAttribute()
    {
        if (!DockerAvailable.Value)
            Skip = "Docker is not available.";
    }

    private static bool CheckDocker()
    {
        try
        {
            var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            });

            if (process is null)
                return false;

            process.WaitForExit(10000);
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Support/CashFlowCollection.cs

```csharp
using Verity.CashFlow.IntegrationTests.Fixtures;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Support;

[CollectionDefinition("cash-flow")]
public sealed class CashFlowCollection : ICollectionFixture<CashFlowContainers>;
```

### tests/Verity.CashFlow.IntegrationTests/Fixtures/CashFlowContainers.cs

```csharp
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Fixtures;

public sealed class CashFlowContainers : IAsyncLifetime
{
    private readonly MsSqlContainer _database = new MsSqlBuilder()
        .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder()
        .WithImage("rabbitmq:3-management")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public string RabbitMqHostName => _rabbitMq.Hostname;

    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await _rabbitMq.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_database.GetConnectionString())
        {
            InitialCatalog = "CashFlowDb"
        }.ConnectionString;
    }

    public async Task DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _database.DisposeAsync();
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Fixtures/EntriesApiFactory.cs

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Verity.CashFlow.IntegrationTests.Fixtures;

public sealed class EntriesApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _rabbitHost;
    private readonly int _rabbitPort;

    private EntriesApiFactory(string connectionString, string rabbitHost, int rabbitPort)
    {
        _connectionString = connectionString;
        _rabbitHost = rabbitHost;
        _rabbitPort = rabbitPort;
    }

    public static EntriesApiFactory For(CashFlowContainers containers,
        string? rabbitHostOverride = null) =>
        new(containers.ConnectionString, rabbitHostOverride ?? containers.RabbitMqHostName,
            containers.RabbitMqPort);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CashFlowDatabase"] = _connectionString,
                ["RabbitMq:HostName"] = _rabbitHost,
                ["RabbitMq:Port"] = _rabbitPort.ToString()
            }));
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Fixtures/ConsolidationApiFactory.cs

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;

namespace Verity.CashFlow.IntegrationTests.Fixtures;

public sealed class ConsolidationApiFactory
    : WebApplicationFactory<Verity.CashFlow.Consolidation.Api.Program>
{
    private readonly string _connectionString;
    private readonly string _rabbitHost;
    private readonly int _rabbitPort;

    private ConsolidationApiFactory(string connectionString, string rabbitHost, int rabbitPort)
    {
        _connectionString = connectionString;
        _rabbitHost = rabbitHost;
        _rabbitPort = rabbitPort;
    }

    public static ConsolidationApiFactory For(CashFlowContainers containers) =>
        new(containers.ConnectionString, containers.RabbitMqHostName,
            containers.RabbitMqPort);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CashFlowDatabase"] = _connectionString,
                ["RabbitMq:HostName"] = _rabbitHost,
                ["RabbitMq:Port"] = _rabbitPort.ToString()
            }));
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Entries/CreateEntryEndpointTests.cs

```csharp
using System.Net;
using System.Net.Http.Json;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class CreateEntryEndpointTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory = EntriesApiFactory.For(containers);
    private readonly HttpClient _client = EntriesApiFactory.For(containers).CreateClient();

    [DockerRequiredFact]
    public async Task Post_WithValidCredit_Returns201WithLocationAndBody()
    {
        var request = new
        {
            amount = 150.75m,
            type = "Credit",
            description = "Cash sale",
            occurredAtUtc = DateTime.UtcNow.AddHours(-1)
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        response.Headers.Location.ShouldNotBeNull();

        var body = await response.Content.ReadFromJsonAsync<EntryResponseTest>();
        body.ShouldNotBeNull();
        body.Amount.ShouldBe(150.75m);
        body.Type.ShouldBe("Credit");
        body.Description.ShouldBe("Cash sale");
        response.Headers.Location!.AbsolutePath.ShouldBe($"/api/entries/{body.Id}");
    }

    [DockerRequiredFact]
    public async Task Post_WithNonPositiveAmount_Returns400WithErrorCode()
    {
        var request = new
        {
            amount = 0m,
            type = "Credit",
            description = "Cash sale"
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsTest>();
        problem.ShouldNotBeNull();
        problem.Extensions.TryGetValue("errorCode", out var errorCode).ShouldBeTrue();
        errorCode!.ToString().ShouldBe("AMOUNT_MUST_BE_POSITIVE");
    }

    [DockerRequiredFact]
    public async Task Post_WithFutureOccurredAt_Returns400WithErrorCode()
    {
        var request = new
        {
            amount = 10m,
            type = "Debit",
            description = "Supplier payment",
            occurredAtUtc = DateTime.UtcNow.AddDays(1)
        };

        var response = await _client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private sealed record EntryResponseTest(
        Guid Id, decimal Amount, string Type, string Description,
        DateTime OccurredAtUtc, DateTime CreatedAtUtc);

    private sealed record ProblemDetailsTest
    {
        public Dictionary<string, object?> Extensions { get; init; } = [];
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Entries/GetEntryEndpointsTests.cs

```csharp
using System.Net;
using System.Net.Http.Json;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class GetEntryEndpointsTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory = EntriesApiFactory.For(containers);
    private readonly HttpClient _client = EntriesApiFactory.For(containers).CreateClient();

    [DockerRequiredFact]
    public async Task GetById_AfterCreate_ReturnsSameEntry()
    {
        var created = await CreateEntryAsync(_client);

        var response = await _client.GetAsync($"/api/entries/{created.Id}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<EntryResponseTest>();
        body.ShouldNotBeNull();
        body.Id.ShouldBe(created.Id);
        body.Amount.ShouldBe(created.Amount);
    }

    [DockerRequiredFact]
    public async Task GetById_WithUnknownId_Returns404WithErrorCode()
    {
        var response = await _client.GetAsync($"/api/entries/{Guid.NewGuid()}");

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetailsTest>();
        problem.ShouldNotBeNull();
        problem.Extensions.TryGetValue("errorCode", out var errorCode).ShouldBeTrue();
        errorCode!.ToString().ShouldBe("ENTRY_NOT_FOUND");
    }

    [DockerRequiredFact]
    public async Task ListByDate_ReturnsOnlyEntriesOfRequestedDate()
    {
        var occurredAt = DateTime.UtcNow.AddDays(-3);
        await CreateEntryAsync(_client, 10m, "Credit", occurredAt);
        await CreateEntryAsync(_client, 4m, "Debit", occurredAt);
        await CreateEntryAsync(_client, 99m, "Credit", DateTime.UtcNow.AddDays(-1));

        var date = DateOnly.FromDateTime(occurredAt);
        var response = await _client.GetAsync($"/api/entries?date={date:yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = await response.Content.ReadFromJsonAsync<List<EntryResponseTest>>();
        body.ShouldNotBeNull();
        body.Count.ShouldBe(2);
        body.ShouldAllBe(entry =>
            DateOnly.FromDateTime(entry.OccurredAtUtc) == date);
    }

    [DockerRequiredFact]
    public async Task ListByDate_WithoutDate_Returns400()
    {
        var response = await _client.GetAsync("/api/entries");

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
    }

    public void Dispose()
    {
        _client.Dispose();
        _factory.Dispose();
    }

    private static async Task<EntryResponseTest> CreateEntryAsync(
        HttpClient client, decimal amount = 100m, string type = "Credit",
        DateTime? occurredAtUtc = null)
    {
        var request = new
        {
            amount,
            type,
            description = "Integration test entry",
            occurredAtUtc = occurredAtUtc ?? DateTime.UtcNow.AddMinutes(-5)
        };

        var response = await client.PostAsJsonAsync("/api/entries", request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<EntryResponseTest>())!;
    }

    private sealed record EntryResponseTest(
        Guid Id, decimal Amount, string Type, string Description,
        DateTime OccurredAtUtc, DateTime CreatedAtUtc);

    private sealed record ProblemDetailsTest
    {
        public Dictionary<string, object?> Extensions { get; init; } = [];
    }
}
```

### tests/Verity.CashFlow.IntegrationTests/Entries/ResilienceTests.cs

```csharp
using System.Net;
using System.Net.Http.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Entries;

[Collection("cash-flow")]
public sealed class ResilienceTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _factory =
        EntriesApiFactory.For(containers, rabbitHostOverride: "broker-unavailable");

    [DockerRequiredFact]
    public async Task Post_WithBrokerUnavailable_Returns201AndKeepsEventInOutbox()
    {
        var client = _factory.CreateClient();
        var request = new
        {
            amount = 200m,
            type = "Credit",
            description = "Broker down scenario"
        };

        var response = await client.PostAsJsonAsync("/api/entries", request);

        response.StatusCode.ShouldBe(HttpStatusCode.Created);

        await using var connection = new SqlConnection(containers.ConnectionString);
        var pending = await connection.ExecuteScalarAsync<int>(
            "SELECT COUNT(*) FROM dbo.outbox_messages WHERE processed_at_utc IS NULL");

        pending.ShouldBeGreaterThanOrEqualTo(1);
    }

    [DockerRequiredFact]
    public async Task Health_Returns200()
    {
        var client = _factory.CreateClient();

        var response = await client.GetAsync("/health");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    public void Dispose() => _factory.Dispose();
}
```

### tests/Verity.CashFlow.IntegrationTests/E2E/CashFlowEndToEndTests.cs

```csharp
using System.Net;
using System.Net.Http.Json;
using Verity.CashFlow.IntegrationTests.Fixtures;
using Verity.CashFlow.IntegrationTests.Support;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.E2E;

[Collection("cash-flow")]
public sealed class CashFlowEndToEndTests(CashFlowContainers containers) : IDisposable
{
    private readonly EntriesApiFactory _entries = EntriesApiFactory.For(containers);
    private readonly ConsolidationApiFactory _consolidation =
        ConsolidationApiFactory.For(containers);

    [DockerRequiredFact]
    public async Task FullFlow_EntryPosted_EndsUpInConsolidatedBalance()
    {
        var entriesClient = _entries.CreateClient();
        var consolidationClient = _consolidation.CreateClient();

        var targetDate = DateTime.UtcNow.AddMinutes(-10);

        await entriesClient.PostAsJsonAsync("/api/entries", new
        {
            amount = 150m,
            type = "Credit",
            description = "Cash sale",
            occurredAtUtc = targetDate
        });

        await entriesClient.PostAsJsonAsync("/api/entries", new
        {
            amount = 30m,
            type = "Debit",
            description = "Supplier payment",
            occurredAtUtc = targetDate
        });

        var date = DateOnly.FromDateTime(targetDate);

        var balance = await WaitForBalanceAsync(consolidationClient, date,
            expected => expected.TotalCredits == 150m && expected.TotalDebits == 30m);

        balance.ShouldNotBeNull();
        balance.DayBalance.ShouldBe(120m);
        balance.AccumulatedBalance.ShouldBe(120m);
    }

    [DockerRequiredFact]
    public async Task FullFlow_DayWithoutEntries_ReturnsZeros()
    {
        var consolidationClient = _consolidation.CreateClient();

        var response = await consolidationClient
            .GetAsync($"/api/consolidated/{new DateOnly(2000, 1, 1):yyyy-MM-dd}");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        var balance = await response.Content
            .ReadFromJsonAsync<ConsolidatedBalanceTest>();
        balance.ShouldNotBeNull();
        balance.TotalCredits.ShouldBe(0m);
        balance.TotalDebits.ShouldBe(0m);
        balance.DayBalance.ShouldBe(0m);
        balance.AccumulatedBalance.ShouldBe(0m);
    }

    public void Dispose()
    {
        _entries.Dispose();
        _consolidation.Dispose();
    }

    private static async Task<ConsolidatedBalanceTest?> WaitForBalanceAsync(
        HttpClient client, DateOnly date,
        Func<ConsolidatedBalanceTest, bool> condition)
    {
        using var timeoutCancellation = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        while (!timeoutCancellation.IsCancellationRequested)
        {
            var response = await client.GetAsync(
                $"/api/consolidated/{date:yyyy-MM-dd}",
                timeoutCancellation.Token);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var balance = await response.Content
                    .ReadFromJsonAsync<ConsolidatedBalanceTest>(timeoutCancellation.Token);
                if (balance is not null && condition(balance))
                    return balance;
            }

            await Task.Delay(500, timeoutCancellation.Token);
        }

        return null;
    }

    private sealed record ConsolidatedBalanceTest(
        DateOnly Date,
        decimal TotalCredits,
        decimal TotalDebits,
        decimal DayBalance,
        decimal AccumulatedBalance);
}
```

## Fase GREEN 2 — rodar até verde

```powershell
dotnet build Verity.CashFlow.sln
dotnet test tests/Verity.CashFlow.UnitTests
dotnet test tests/Verity.CashFlow.IntegrationTests
```

Sem Docker: os testes de integração exibem skip (mensagem "Docker is not available.") —
comportamento esperado e documentado.

## Critérios de aceite

1. Unitários das ondas 2–6 continuam verdes.
2. Com Docker: todos os testes de integração verdes, incluindo o e2e completo e o
   cenário de resiliência (201 com broker fora + linha pendente no outbox).
3. Endpoints mapeiam `Result` via pattern match — sem try/catch de domínio.
4. Erros de domínio → 400 com `errorCode`; `ENTRY_NOT_FOUND` → 404 com `errorCode`;
   bind de enum aceita `"Credit"`/`"Debit"`.
5. OpenAPI (`/openapi/v1.json` em Development) descreve endpoints com summaries.
6. `/health` responde 200 nas duas APIs.

## Notas / riscos

- **Aguardar MSSQL no container:** Testcontainers já aguarda o container ficar pronto;
  o `DatabaseInitializer` do `Program.cs` cria banco/tabelas em qualquer startup
  (idempotente) — nos testes ele roda durante o boot da factory.
- **Resiliência com ruído de log esperado:** no teste com broker fora, o dispatcher
  falha ao publicar e loga erro a cada ciclo (2s) — isso É o comportamento testado;
  a mensagem permanece pendente.
- **Fixtures por classe de teste:** cada classe cria suas factories a partir dos
  containers compartilhados do collection fixture (1 MSSQL + 1 RabbitMQ para tudo).
  Factories são descartadas no `Dispose` da classe; containers vivem durante a collection.
- `Program` das duas APIs no mesmo projeto de teste: desambiguado por namespace
  (`Verity.CashFlow.Entries.Api.Program` implícito em `EntriesApiFactory`;
  explícito em `ConsolidationApiFactory`).
- Se `DateOnly` não bindar em query string em alguma versão, registrar conversor custom
  (validar com Learn MCP na implementação).
- O e2e compartilha o banco entre as duas APIs (CashFlowDb) e o RabbitMQ — topologia
  declarada pelo consumer da Consolidation.
