using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.Consolidation.Api.Endpoints;

public sealed record ConsolidatedBalanceResponse(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal DayBalance,
    decimal AccumulatedBalance)
{
    public static ConsolidatedBalanceResponse From(DailyConsolidatedBalance balance) =>
        new(balance.Date, balance.TotalCredits, balance.TotalDebits,
            balance.DayBalance, balance.AccumulatedBalance);
}
