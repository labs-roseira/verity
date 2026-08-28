using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.UnitTests.Application;

public class CreateEntryUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();
    private readonly TimeProvider _clock = Substitute.For<TimeProvider>();

    private CreateEntryUseCase CreateSut() => new(_entryStore, _clock);

    private void SetupClock(DateTimeOffset utcNow)
    {
        _clock.GetUtcNow().Returns(utcNow);
        _clock.LocalTimeZone.Returns(TimeZoneInfo.Utc);
    }

    [Fact]
    public async Task ExecuteAsync_WithValidInput_PersistsEntryWithOutboxEvent()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(100m, EntryType.Credit, "Cash sale", null,
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _entryStore.Received(1).SaveWithOutboxAsync(
            Arg.Is<Entry>(e =>
                e.Id == result.Value.Id &&
                e.Amount == 100m &&
                e.Type == EntryType.Credit &&
                e.Description == "Cash sale"),
            Arg.Is<EntryCreated>(ev =>
                ev.EntryId == result.Value.Id &&
                ev.Amount == 100m &&
                ev.Type == EntryType.Credit &&
                ev.Description == "Cash sale" &&
                ev.OccurredAtUtc == utcNow.UtcDateTime.ToLocalTime()),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOccurredAt_UsesCurrentLocalTime()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment", null,
            CancellationToken.None);

        result.Value.OccurredAtUtc.ShouldBe(utcNow.UtcDateTime.ToLocalTime());
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitOccurredAt_KeepsProvidedDate()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var occurredAt = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment",
            occurredAt, CancellationToken.None);

        result.Value.OccurredAtUtc.ShouldBe(occurredAt);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidDomainInput_ReturnsFailureWithoutPersisting()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(0m, EntryType.Credit, "Cash sale", null,
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
        await _entryStore.DidNotReceiveWithAnyArgs().SaveWithOutboxAsync(
            default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WithFutureOccurredAt_ReturnsFailureWithCode()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(10m, EntryType.Credit, "Cash sale",
            utcNow.UtcDateTime.AddDays(1), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OCCURRED_AT_IN_FUTURE");
    }
}
