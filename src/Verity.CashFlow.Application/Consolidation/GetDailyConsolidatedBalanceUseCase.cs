using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Application.Consolidation;

public sealed class GetDailyConsolidatedBalanceUseCase(
    IConsolidatedBalanceReader reader)
{
    public async Task<DailyConsolidatedBalance> ExecuteAsync(DateOnly date,
        CancellationToken cancellationToken)
    {
        var snapshot = await reader.GetByDateAsync(date, cancellationToken);

        var accumulated = await reader.GetAccumulatedBalanceAsync(date, cancellationToken);

        var totalCredits = snapshot?.TotalCredits ?? 0m;
        var totalDebits = snapshot?.TotalDebits ?? 0m;

        return new DailyConsolidatedBalance(
            date,
            totalCredits,
            totalDebits,
            totalCredits - totalDebits,
            accumulated);
    }
}
