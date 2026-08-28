using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Scalar.AspNetCore;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Entries.Api.Endpoints;
using Verity.CashFlow.Infrastructure.Messaging;
using Verity.CashFlow.Infrastructure.Persistence;

namespace Verity.CashFlow.Entries.Api;

public sealed class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.Services.AddOpenApi();
        builder.Services.AddEndpointsApiExplorer();
        builder.Services.AddSwaggerGen();

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

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapEntryEndpoints();

        app.MapGet("/health", () => Results.Ok(new { status = "Ok" }))
            .WithSummary("Liveness probe.")
            .ExcludeFromDescription();

        app.Run();
    }
}
