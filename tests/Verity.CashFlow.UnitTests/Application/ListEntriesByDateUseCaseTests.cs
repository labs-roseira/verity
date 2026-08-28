using NSubstitute;
using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;

namespace Verity.CashFlow.UnitTests.Application;

public class ListEntriesByDateUseCaseTests
{
    private readonly IEntryStore _entryStore = Substitute.For<IEntryStore>();

    [Fact]
    public async Task ExecuteAsync_DelegatesDateAndPagingToStore()
    {
        var date = new DateOnly(2026, 1, 15);
        var page = 2;
        var pageSize = 25;
        var entries = new List<Entry>
        {
            Entry.Create(10m, EntryType.Credit, "Cash sale",
                new DateTime(2026, 1, 15, 9, 0, 0, DateTimeKind.Utc),
                new DateTime(2026, 1, 15, 12, 0, 0, DateTimeKind.Utc)).Value
        };
        _entryStore.ListByDateAsync(date, page, pageSize, Arg.Any<CancellationToken>())
            .Returns(entries);
        var sut = new ListEntriesByDateUseCase(_entryStore);

        var result = await sut.ExecuteAsync(date, page, pageSize, CancellationToken.None);

        result.ShouldBe(entries);
        await _entryStore.Received(1).ListByDateAsync(date, page, pageSize,
            Arg.Any<CancellationToken>());
    }
}
