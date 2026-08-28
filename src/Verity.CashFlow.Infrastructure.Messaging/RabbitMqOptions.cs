namespace Verity.CashFlow.Infrastructure.Messaging;

public sealed class RabbitMqOptions
{
    public const string SectionName = "RabbitMq";

    public string HostName { get; init; } = "localhost";

    public int Port { get; init; } = 5672;

    public string UserName { get; init; } = "guest";

    public string Password { get; init; } = "guest";

    public string EntriesExchange { get; init; } = "entries.events";

    public string EntryCreatedRoutingKey { get; init; } = "entry.created";

    public string EntryCreatedQueue { get; init; } = "entry.created";

    public string DeadLetterExchange { get; init; } = "entries.events.dlx";

    public string DeadLetterQueue { get; init; } = "entry.created.dead";
}
