namespace Verity.CashFlow.Application.IntegrationEvents;

public interface IOutboxStore
{
    Task<IReadOnlyList<PendingOutboxMessage>> GetPendingAsync(int batchSize,
        CancellationToken cancellationToken);

    Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken);
}
