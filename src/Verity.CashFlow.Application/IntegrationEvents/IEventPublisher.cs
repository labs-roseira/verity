namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IEventPublisher
{
    Task PublishAsync(string type, string payload, CancellationToken cancellationToken);
}
