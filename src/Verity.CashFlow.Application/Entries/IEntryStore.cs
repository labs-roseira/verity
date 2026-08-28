using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.Entries;

public interface IEntryStore
{
    Task<Guid> SaveWithOutboxAsync(Entry entry, EntryCreated @event, CancellationToken cancellationToken);

    Task<Entry?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<Entry>> ListByDateAsync(DateOnly date, int page, int pageSize,
        CancellationToken cancellationToken);
}
