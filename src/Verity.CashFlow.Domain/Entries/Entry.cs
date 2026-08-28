using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Domain.Entries;

public sealed class Entry
{
    public const int DescriptionMaxLength = 500;

    private Entry(Guid id, decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime createdAtUtc)
    {
        Id = id;
        Amount = amount;
        Type = type;
        Description = description;
        OccurredAtUtc = occurredAtUtc;
        CreatedAtUtc = createdAtUtc;
    }

    public Guid Id { get; }

    public decimal Amount { get; }

    public EntryType Type { get; }

    public string Description { get; }

    public DateTime OccurredAtUtc { get; }

    public DateTime CreatedAtUtc { get; }

    public static Result<Entry> Create(decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime utcNow)
    {
        if (amount <= 0)
            return Result.Failure<Entry>(EntryErrors.AmountMustBePositive);

        if (!Enum.IsDefined(type))
            return Result.Failure<Entry>(EntryErrors.TypeInvalid);

        if (string.IsNullOrWhiteSpace(description))
            return Result.Failure<Entry>(EntryErrors.DescriptionRequired);

        if (description.Length > DescriptionMaxLength)
            return Result.Failure<Entry>(EntryErrors.DescriptionTooLong);

        if (occurredAtUtc > utcNow)
            return Result.Failure<Entry>(EntryErrors.OccurredAtInFuture);

        return Result.Success(new Entry(Guid.NewGuid(), amount, type, description.Trim(),
            occurredAtUtc, utcNow));
    }

    public static Entry Restore(Guid id, decimal amount, EntryType type, string description,
        DateTime occurredAtUtc, DateTime createdAtUtc)
    {
        return new Entry(id, amount, type, description, occurredAtUtc, createdAtUtc);
    }
}
