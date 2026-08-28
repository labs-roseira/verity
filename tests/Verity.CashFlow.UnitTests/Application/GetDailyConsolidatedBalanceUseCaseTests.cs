using NSubstitute;
using Verity.CashFlow.Application.Consolidation;
using Verity.CashFlow.Domain.Consolidation;

namespace Verity.CashFlow.UnitTests.Application;

public class GetDailyConsolidatedBalanceUseCaseTests
{
    private readonly IConsolidatedBalanceReader _reader =
        Substitute.For<IConsolidatedBalanceReader>();

    [Fact]
    public async Task ExecuteAsync_WhenDateHasNoData_ReturnsAllZeros()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceSnapshot?)null);
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(0m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.Date.ShouldBe(date);
        result.TotalCredits.ShouldBe(0m);
        result.TotalDebits.ShouldBe(0m);
        result.DayBalance.ShouldBe(0m);
        result.AccumulatedBalance.ShouldBe(0m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDayTotals_ComputesDayBalance()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new DailyBalanceSnapshot(date, 150.75m, 30m));
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(120.75m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.TotalCredits.ShouldBe(150.75m);
        result.TotalDebits.ShouldBe(30m);
        result.DayBalance.ShouldBe(120.75m);
        result.AccumulatedBalance.ShouldBe(120.75m);
    }

    [Fact]
    public async Task ExecuteAsync_WithDebitsGreaterThanCredits_ReturnsNegativeDayBalance()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns(new DailyBalanceSnapshot(date, 50m, 80m));
        _reader.GetAccumulatedBalanceAsync(date, Arg.Any<CancellationToken>())
            .Returns(-30m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        var result = await sut.ExecuteAsync(date, CancellationToken.None);

        result.DayBalance.ShouldBe(-30m);
        result.AccumulatedBalance.ShouldBe(-30m);
    }

    [Fact]
    public async Task ExecuteAsync_QueriesAccumulatedUpToRequestedDate()
    {
        var date = new DateOnly(2026, 8, 25);
        _reader.GetByDateAsync(date, Arg.Any<CancellationToken>())
            .Returns((DailyBalanceSnapshot?)null);
        _reader.GetAccumulatedBalanceAsync(Arg.Any<DateOnly>(), Arg.Any<CancellationToken>())
            .Returns(0m);
        var sut = new GetDailyConsolidatedBalanceUseCase(_reader);

        await sut.ExecuteAsync(date, CancellationToken.None);

        await _reader.Received(1).GetAccumulatedBalanceAsync(date,
            Arg.Any<CancellationToken>());
    }
}
