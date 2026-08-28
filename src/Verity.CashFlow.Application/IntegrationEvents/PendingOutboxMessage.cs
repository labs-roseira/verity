namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record PendingOutboxMessage(Guid Id, string Type, string Payload);
