using Verity.CashFlow.Application.Consolidation;

namespace Verity.CashFlow.Consolidation.Api.Endpoints;

public static class ConsolidatedBalanceEndpoints
{
    public static IEndpointRouteBuilder MapConsolidatedBalanceEndpoints(
        this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/consolidated").WithTags("Consolidation");

        group.MapGet("/{date}", GetDailyConsolidatedBalance)
            .WithName("GetDailyConsolidatedBalance")
            .WithSummary("Get the consolidated daily balance for a given date.")
            .WithDescription(
                "Returns total credits, total debits, the day balance and the accumulated "
                + "balance up to the requested date. Dates without entries return zeros.")
            .Produces<ConsolidatedBalanceResponse>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> GetDailyConsolidatedBalance(
        DateOnly date,
        GetDailyConsolidatedBalanceUseCase getDailyConsolidatedBalance,
        CancellationToken cancellationToken)
    {
        var balance = await getDailyConsolidatedBalance.ExecuteAsync(
            date, cancellationToken);

        return Results.Ok(ConsolidatedBalanceResponse.From(balance));
    }
}
