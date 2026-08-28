namespace Verity.CashFlow.Domain.Consolidation;

public sealed record DailyBalanceSnapshot(
    DateOnly Date,
    decimal TotalCredits,
    decimal TotalDebits);
