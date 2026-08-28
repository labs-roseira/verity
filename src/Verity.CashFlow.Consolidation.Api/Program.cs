using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Scalar.AspNetCore;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Consolidation.Api.Endpoints;
using Verity.CashFlow.Infrastructure.Messaging;
using Verity.CashFlow.Infrastructure.Persistence;

namespace Verity.CashFlow.Consolidation.Api;

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

        if (!app.Environment.IsProduction())
        {
            app.MapOpenApi();
            app.MapScalarApiReference();
            app.UseSwagger();
            app.UseSwaggerUI();
        }

        app.MapConsolidatedBalanceEndpoints();

        app.MapGet("/health", () => Results.Ok(new { status = "Ok" }))
            .WithSummary("Liveness probe.")
            .ExcludeFromDescription();

        app.Run();
    }
}
