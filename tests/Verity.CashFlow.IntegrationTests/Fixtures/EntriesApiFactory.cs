using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Verity.CashFlow.Entries.Api;

namespace Verity.CashFlow.IntegrationTests.Fixtures;

public sealed class EntriesApiFactory : WebApplicationFactory<Program>
{
    private readonly string _connectionString;
    private readonly string _rabbitHost;
    private readonly int _rabbitPort;
    private readonly string _rabbitUser;
    private readonly string _rabbitPass;

    private EntriesApiFactory(string connectionString, string rabbitHost, int rabbitPort,
        string rabbitUser, string rabbitPass)
    {
        _connectionString = connectionString;
        _rabbitHost = rabbitHost;
        _rabbitPort = rabbitPort;
        _rabbitUser = rabbitUser;
        _rabbitPass = rabbitPass;

        Environment.SetEnvironmentVariable("ConnectionStrings__CashFlowDatabase", _connectionString);
        Environment.SetEnvironmentVariable("RabbitMq__HostName", _rabbitHost);
        Environment.SetEnvironmentVariable("RabbitMq__Port", _rabbitPort.ToString());
        Environment.SetEnvironmentVariable("RabbitMq__UserName", _rabbitUser);
        Environment.SetEnvironmentVariable("RabbitMq__Password", _rabbitPass);
    }

    public static EntriesApiFactory For(CashFlowContainers containers,
        string? rabbitHostOverride = null) =>
        new(containers.ConnectionString,
            rabbitHostOverride ?? containers.RabbitMqHostName,
            containers.RabbitMqPort,
            containers.RabbitMqUserName,
            containers.RabbitMqPassword);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, configuration) =>
            configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:CashFlowDatabase"] = _connectionString,
                ["RabbitMq:HostName"] = _rabbitHost,
                ["RabbitMq:Port"] = _rabbitPort.ToString(),
                ["RabbitMq:UserName"] = _rabbitUser,
                ["RabbitMq:Password"] = _rabbitPass
            }));
    }
}
