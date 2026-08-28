using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Application.Consolidation;

public interface IConsolidatedBalanceReader
{
    Task<DailyBalanceSnapshot?> GetByDateAsync(DateOnly date,
        CancellationToken cancellationToken);

    Task<decimal> GetAccumulatedBalanceAsync(DateOnly upToDate,
        CancellationToken cancellationToken);
}
