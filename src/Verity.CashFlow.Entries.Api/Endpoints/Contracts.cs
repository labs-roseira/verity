using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Entries.Api.Endpoints;

public sealed record CreateEntryRequest(
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime? OccurredAtUtc);

public sealed record EntryResponse(
    Guid Id,
    decimal Amount,
    EntryType Type,
    string Description,
    DateTime OccurredAtUtc,
    DateTime CreatedAtUtc)
{
    public static EntryResponse From(Entry entry) =>
        new(entry.Id, entry.Amount, entry.Type, entry.Description,
            entry.OccurredAtUtc, entry.CreatedAtUtc);
}
