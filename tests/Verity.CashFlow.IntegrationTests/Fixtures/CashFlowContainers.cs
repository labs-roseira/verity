using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Verity.CashFlow.IntegrationTests.Fixtures;

public sealed class CashFlowContainers : IAsyncLifetime
{
    private readonly MsSqlContainer _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    private readonly RabbitMqContainer _rabbitMq = new RabbitMqBuilder("rabbitmq:3-management")
        .Build();

    public string ConnectionString { get; private set; } = string.Empty;

    public string RabbitMqHostName => _rabbitMq.Hostname;

    public int RabbitMqPort => _rabbitMq.GetMappedPublicPort(5672);

    public string RabbitMqUserName { get; private set; } = "guest";

    public string RabbitMqPassword { get; private set; } = "guest";

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
        await _rabbitMq.StartAsync();

        ConnectionString = new SqlConnectionStringBuilder(_database.GetConnectionString())
        {
            InitialCatalog = "CashFlowDb"
        }.ConnectionString;

        var amqpUri = new Uri(_rabbitMq.GetConnectionString());
        var userInfo = amqpUri.UserInfo.Split(':', 2);
        RabbitMqUserName = userInfo[0];
        RabbitMqPassword = userInfo.Length > 1 ? userInfo[1] : "";
    }

    public async Task DisposeAsync()
    {
        await _rabbitMq.DisposeAsync();
        await _database.DisposeAsync();
    }
}
