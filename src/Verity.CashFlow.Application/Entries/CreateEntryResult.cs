using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.Application.Entries;

public sealed record CreateEntryResult(Entry Entry, bool WasCreated);
