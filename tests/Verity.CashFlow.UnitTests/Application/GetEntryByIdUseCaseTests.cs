using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.UnitTests.Application;

public class GetEntryByIdUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();

    [Fact]
    public async Task ExecuteAsync_WhenEntryExists_ReturnsSuccessWithEntry()
    {
        var entry = Entry.Create(100m, EntryType.Credit, "Cash sale",
            new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Value;
        _entryStore.GetByIdAsync(entry.Id, Arg.Any<CancellationToken>()).Returns(entry);
        var sut = new GetEntryByIdUseCase(_entryStore);

        var result = await sut.ExecuteAsync(entry.Id, CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(entry);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEntryDoesNotExist_ReturnsFailureWithCode()
    {
        _entryStore.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Entry?)null);
        var sut = new GetEntryByIdUseCase(_entryStore);

        var result = await sut.ExecuteAsync(Guid.NewGuid(), CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        result.Error.Code.ShouldBe("ENTRY_NOT_FOUND");
    }
}
