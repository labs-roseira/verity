using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Application.IntegrationEvents;
using Verity.CashFlow.Domain.Entries;
using Verity.CashFlow.Infrastructure.Messaging;

namespace Verity.CashFlow.UnitTests.Messaging;

public class EntryCreatedProcessorTests
{
    private readonly IEntryProjection _entryProjection = Substitute.For<IEntryProjection>();

    private EntryCreatedProcessor CreateSut() =>
        new(_entryProjection, NullLogger<EntryCreatedProcessor>.Instance,
            retryDelay: TimeSpan.Zero);

    private static ReadOnlyMemory<byte> Serialize(EntryCreated @event)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        return Encoding.UTF8.GetBytes(JsonSerializer.Serialize(@event, options));
    }

    [Fact]
    public async Task ProcessAsync_WithNewEvent_ProjectsAndAcknowledges()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection.ApplyAsync(@event, Arg.Any<CancellationToken>()).Returns(true);
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
    }

    [Fact]
    public async Task ProcessAsync_WithDuplicateEvent_AcknowledgesWithoutReprojection()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection.ApplyAsync(@event, Arg.Any<CancellationToken>()).Returns(false);
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
        await _entryProjection.Received(1).ApplyAsync(@event, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("not a json")]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("""{"entryId":"not-a-guid"}""")]
    public async Task ProcessAsync_WithInvalidPayload_DeadLettersWithoutProjection(string payload)
    {
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Encoding.UTF8.GetBytes(payload),
            CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.DeadLetter);
        await _entryProjection.DidNotReceiveWithAnyArgs().ApplyAsync(default!,
            default);
    }

    [Fact]
    public async Task ProcessAsync_WhenProjectionAlwaysFails_DeadLettersAfterMaxAttempts()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Debit, "Supplier payment",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        _entryProjection
            .WhenForAnyArgs(projection => projection.ApplyAsync(default!, default))
            .Do(_ => throw new InvalidOperationException("database down"));
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.DeadLetter);
        await _entryProjection.Received(EntryCreatedProcessor.MaxAttempts).ApplyAsync(
            @event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WhenProjectionFailsThenSucceeds_RetriesAndAcknowledges()
    {
        var @event = new EntryCreated(Guid.NewGuid(), 100m, EntryType.Debit, "Supplier payment",
            new DateTime(2026, 8, 25, 14, 0, 0, DateTimeKind.Utc));
        var attempts = 0;
        _entryProjection
            .WhenForAnyArgs(projection => projection.ApplyAsync(default!, default))
            .Do(_ =>
            {
                attempts++;
                if (attempts < 3)
                    throw new InvalidOperationException("transient failure");
            });
        var sut = CreateSut();

        var decision = await sut.ProcessAsync(Serialize(@event), CancellationToken.None);

        decision.ShouldBe(ProcessingDecision.Acknowledge);
        attempts.ShouldBe(3);
    }
}
