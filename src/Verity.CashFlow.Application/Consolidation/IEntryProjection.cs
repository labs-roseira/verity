using Verity.CashFlow.Application.IntegrationEvents;

namespace Verity.CashFlow.Application.Consolidation;

public interface IEntryProjection
{
    Task<bool> ApplyAsync(EntryCreated @event, CancellationToken cancellationToken);
}
