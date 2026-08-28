using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Infrastructure.Messaging;

namespace Verity.CashFlow.UnitTests.Messaging;

public class OutboxDispatcherTests
{
    private readonly IOutboxStore _outboxStore = Substitute.For<IOutboxStore>();
    private readonly IEventPublisher _eventPublisher = Substitute.For<IEventPublisher>();

    private OutboxDispatcher CreateSut() =>
        new(_outboxStore, _eventPublisher, NullLogger<OutboxDispatcher>.Instance);

    private static PendingOutboxMessage NewMessage(Guid id) =>
        new(id, "EntryCreated", $"{{\"entryId\":\"{id}\"}}");

    [Fact]
    public async Task DispatchPendingAsync_WithPendingMessages_PublishesAndMarksEachProcessed()
    {
        var first = NewMessage(Guid.NewGuid());
        var second = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { first, second });
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _eventPublisher.Received(1).PublishAsync(first.Type, first.Payload,
            Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(second.Type, second.Payload,
            Arg.Any<CancellationToken>());
        await _outboxStore.Received(1).MarkProcessedAsync(first.Id, Arg.Any<CancellationToken>());
        await _outboxStore.Received(1).MarkProcessedAsync(second.Id, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_WithEmptyBatch_PublishesNothing()
    {
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage>());
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _eventPublisher.DidNotReceiveWithAnyArgs().PublishAsync(default!, default!,
            default);
        await _outboxStore.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenPublisherFails_DoesNotMarkProcessed()
    {
        var message = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { message });
        _eventPublisher
            .WhenForAnyArgs(publisher => publisher.PublishAsync(default!, default!, default))
            .Do(_ => throw new InvalidOperationException("broker unavailable"));
        var sut = CreateSut();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.DispatchPendingAsync(CancellationToken.None));

        await _outboxStore.DidNotReceiveWithAnyArgs().MarkProcessedAsync(default, default);
    }

    [Fact]
    public async Task DispatchPendingAsync_WhenPublisherFailsOnSecondMessage_FirstIsStillProcessed()
    {
        var first = NewMessage(Guid.NewGuid());
        var second = NewMessage(Guid.NewGuid());
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage> { first, second });
        _eventPublisher
            .When(publisher => publisher.PublishAsync(second.Type, second.Payload,
                Arg.Any<CancellationToken>()))
            .Do(_ => throw new InvalidOperationException("broker unavailable"));
        var sut = CreateSut();

        await Should.ThrowAsync<InvalidOperationException>(() =>
            sut.DispatchPendingAsync(CancellationToken.None));

        await _outboxStore.Received(1).MarkProcessedAsync(first.Id, Arg.Any<CancellationToken>());
        await _outboxStore.DidNotReceive().MarkProcessedAsync(second.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_RequestsConfiguredBatchSize()
    {
        _outboxStore.GetPendingAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<PendingOutboxMessage>());
        var sut = CreateSut();

        await sut.DispatchPendingAsync(CancellationToken.None);

        await _outboxStore.Received(1).GetPendingAsync(OutboxDispatcher.BatchSize,
            Arg.Any<CancellationToken>());
    }
}
