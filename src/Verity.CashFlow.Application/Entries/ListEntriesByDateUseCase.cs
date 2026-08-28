using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.Entries;

public sealed class ListEntriesByDateUseCase(IEntryStore entryStore)
{
    public Task<IReadOnlyList<Entry>> ExecuteAsync(DateOnly date, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        return entryStore.ListByDateAsync(date, page, pageSize, cancellationToken);
    }
}
