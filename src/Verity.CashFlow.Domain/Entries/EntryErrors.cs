using Verity.CashFlow.Domain.Results;

namespace Verity.CashFlow.Domain.Entries;

public static class EntryErrors
{
    public static Error AmountMustBePositive { get; } =
        new("AMOUNT_MUST_BE_POSITIVE", "Entry amount must be greater than zero.");

    public static Error TypeInvalid { get; } =
        new("ENTRY_TYPE_INVALID", "Entry type must be Credit or Debit.");

    public static Error DescriptionRequired { get; } =
        new("DESCRIPTION_REQUIRED", "Entry description is required.");

    public static Error DescriptionTooLong { get; } =
        new("DESCRIPTION_TOO_LONG", "Entry description must have at most 500 characters.");

    public static Error OccurredAtInFuture { get; } =
        new("OCCURRED_AT_IN_FUTURE", "Entry occurrence date cannot be in the future.");

    public static Error NotFound { get; } =
        new("ENTRY_NOT_FOUND", "Entry was not found.");
}
