using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.UnitTests.Domain;

public class EntryTests
{
    private static readonly DateTime UtcNow = new(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Create_WithValidCredit_ReturnsSuccessWithEntry()
    {
        var occurredAt = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

        var result = Entry.Create(100.50m, EntryType.Credit, "Cash sale", occurredAt, UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Id.ShouldNotBe(Guid.Empty);
        result.Value.Amount.ShouldBe(100.50m);
        result.Value.Type.ShouldBe(EntryType.Credit);
        result.Value.Description.ShouldBe("Cash sale");
        result.Value.OccurredAtUtc.ShouldBe(occurredAt);
        result.Value.CreatedAtUtc.ShouldBe(UtcNow);
    }

    [Fact]
    public void Create_WithValidDebit_ReturnsSuccessWithDebitType()
    {
        var result = Entry.Create(40m, EntryType.Debit, "Supplier payment",
            UtcNow.AddHours(-1), UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Type.ShouldBe(EntryType.Debit);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-0.01)]
    [InlineData(-100)]
    public void Create_WithNonPositiveAmount_ReturnsFailureWithCode(decimal amount)
    {
        var result = Entry.Create(amount, EntryType.Credit, "Cash sale",
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
    }

    [Fact]
    public void Create_WithUndefinedType_ReturnsFailureWithCode()
    {
        var result = Entry.Create(100m, (EntryType)99, "Cash sale",
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ENTRY_TYPE_INVALID");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_WithMissingDescription_ReturnsFailureWithCode(string? description)
    {
        var result = Entry.Create(100m, EntryType.Debit, description!,
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DESCRIPTION_REQUIRED");
    }

    [Fact]
    public void Create_WithDescriptionLongerThanMaxLength_ReturnsFailureWithCode()
    {
        var description = new string('a', 501);

        var result = Entry.Create(100m, EntryType.Debit, description,
            UtcNow.AddHours(-1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("DESCRIPTION_TOO_LONG");
    }

    [Fact]
    public void Create_WithDescriptionAtMaxLength_ReturnsSuccess()
    {
        var description = new string('a', 500);

        var result = Entry.Create(100m, EntryType.Credit, description,
            UtcNow.AddHours(-1), UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Description.Length.ShouldBe(500);
    }

    [Fact]
    public void Create_WithFutureOccurrenceDate_ReturnsFailureWithCode()
    {
        var result = Entry.Create(100m, EntryType.Credit, "Cash sale",
            UtcNow.AddMinutes(1), UtcNow);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OCCURRED_AT_IN_FUTURE");
    }

    [Fact]
    public void Create_WithOccurrenceExactlyNow_ReturnsSuccess()
    {
        var result = Entry.Create(100m, EntryType.Credit, "Cash sale", UtcNow, UtcNow);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OccurredAtUtc.ShouldBe(UtcNow);
    }

    [Fact]
    public void Create_WithSurroundingWhitespace_TrimsDescription()
    {
        var result = Entry.Create(100m, EntryType.Credit, "  Cash sale  ",
            UtcNow.AddHours(-1), UtcNow);

        result.Value.Description.ShouldBe("Cash sale");
    }

    [Fact]
    public void Restore_WithPersistedValues_ReturnsEntryWithSameIdentity()
    {
        var id = Guid.NewGuid();

        var entry = Entry.Restore(id, 50m, EntryType.Debit, "Supplier payment",
            UtcNow.AddHours(-2), UtcNow.AddHours(-1));

        entry.Id.ShouldBe(id);
        entry.Amount.ShouldBe(50m);
        entry.Type.ShouldBe(EntryType.Debit);
        entry.Description.ShouldBe("Supplier payment");
    }
}
