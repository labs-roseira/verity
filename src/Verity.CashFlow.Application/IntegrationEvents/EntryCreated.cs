using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.IntegrationEvents;

public sealed record EntryCreated(
    Guid EntryId,
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime OccurredAtUtc);
