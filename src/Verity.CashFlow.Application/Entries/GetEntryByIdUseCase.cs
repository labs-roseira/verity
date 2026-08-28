using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Application.Entries;

public sealed class GetEntryByIdUseCase(IEntryStore entryStore)
{
    public async Task<Result<Entry>> ExecuteAsync(Guid id, CancellationToken cancellationToken)
    {
        var entry = await entryStore.GetByIdAsync(id, cancellationToken);

        return entry is null
            ? Result.Failure<Entry>(EntryErrors.NotFound)
            : Result.Success(entry);
    }
}
