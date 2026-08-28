namespace Verity.CashFlow.Domain.Consolidation;

public sealed record DailyConsolidatedBalance(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits,
    decimal DayBalance,
    decimal AccumulatedBalance);
