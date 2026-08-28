using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.UnitTests.Application;

public class CreateEntryUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();
    private readonly IOutboxStore _outboxStore = Substitute.For<IOutboxStore>();
    private readonly TimeProvider _clock = Substitute.For<TimeProvider>();
    private readonly ILogger<CreateEntryUseCase> _logger =
        Substitute.For<ILogger<CreateEntryUseCase>>();

    private CreateEntryUseCase CreateSut() =>
        new(_entryStore, _eventPublisher, _outboxStore, _clock, _logger);

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
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(100m, EntryType.Credit, "Cash sale", null,
            null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasCreated.ShouldBeTrue();
        await _entryStore.Received(1).SaveWithOutboxAsync(
            Arg.Is<Entry>(e => e.Amount == 100m && e.Type == EntryType.Credit),
            Arg.Any<EntryCreated>(),
            Arg.Is<string?>(k => k == null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_AfterSave_PublishesInlineAndMarksProcessed()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var outboxId = Guid.NewGuid();
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(outboxId);
        var sut = CreateSut();

        await sut.ExecuteAsync(100m, EntryType.Credit, "Cash sale", null,
            null, CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(
            EventTypes.EntryCreated, Arg.Any<string>(), Arg.Any<CancellationToken>());
        await _outboxStore.Received(1).MarkProcessedAsync(
            outboxId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenInlinePublishFails_ReturnsSuccessAndLeavesOutboxPending()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        _eventPublisher.PublishAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("broker down"));
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(100m, EntryType.Credit, "Cash sale", null,
            null, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        await _outboxStore.DidNotReceiveWithAnyArgs().MarkProcessedAsync(
            default, default);
    }

    [Fact]
    public async Task ExecuteAsync_WithIdempotencyKey_AndKeyExists_ReturnsExistingEntryWithoutSave()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var existingEntry = Entry.Create(100m, EntryType.Credit, "Original",
            new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Value;
        _entryStore.GetByIdempotencyKeyAsync("client-key-123", Arg.Any<CancellationToken>())
            .Returns(existingEntry);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(200m, EntryType.Debit, "Different", null,
            "client-key-123", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasCreated.ShouldBeFalse();
        result.Value.Entry.Id.ShouldBe(existingEntry.Id);
        await _entryStore.DidNotReceiveWithAnyArgs().SaveWithOutboxAsync(
            default!, default!, default, default);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(
            default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WithIdempotencyKey_AndKeyNotFound_CreatesNewAndSavesKey()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        _entryStore.GetByIdempotencyKeyAsync("new-key", Arg.Any<CancellationToken>())
            .Returns((Entry?)null);
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Credit, "New entry", null,
            "new-key", CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.WasCreated.ShouldBeTrue();
        await _entryStore.Received(1).SaveWithOutboxAsync(
            Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
            Arg.Is<string?>(k => k == "new-key"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithoutOccurredAt_UsesCurrentLocalTime()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment", null,
            null, CancellationToken.None);

        result.Value.Entry.OccurredAtUtc.ShouldBe(utcNow.UtcDateTime.ToLocalTime());
    }

    [Fact]
    public async Task ExecuteAsync_WithExplicitOccurredAt_KeepsProvidedDate()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        var occurredAt = new DateTime(2026, 1, 15, 8, 0, 0, DateTimeKind.Utc);
        SetupClock(utcNow);
        _entryStore.SaveWithOutboxAsync(
                Arg.Any<Entry>(), Arg.Any<EntryCreated>(),
                Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(Guid.NewGuid());
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(50m, EntryType.Debit, "Supplier payment",
            occurredAt, null, CancellationToken.None);

        result.Value.Entry.OccurredAtUtc.ShouldBe(occurredAt);
    }

    [Fact]
    public async Task ExecuteAsync_WithInvalidDomainInput_ReturnsFailureWithoutPersisting()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(0m, EntryType.Credit, "Cash sale", null,
            null, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("AMOUNT_MUST_BE_POSITIVE");
        await _entryStore.DidNotReceiveWithAnyArgs().SaveWithOutboxAsync(
            default!, default!, default, default);
        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(
            default!, default!, default);
    }

    [Fact]
    public async Task ExecuteAsync_WithFutureOccurredAt_ReturnsFailureWithCode()
    {
        var utcNow = new DateTimeOffset(2026, 1, 15, 12, 0, 0, TimeSpan.Zero);
        SetupClock(utcNow);
        var sut = CreateSut();

        var result = await sut.ExecuteAsync(10m, EntryType.Credit, "Cash sale",
            utcNow.UtcDateTime.AddDays(1), null, CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("OCCURRED_AT_IN_FUTURE");
    }
}
