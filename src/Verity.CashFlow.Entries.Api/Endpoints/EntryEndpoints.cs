using Verity.CashFlow.Application.Entries;
using Verity.CashFlow.Domain.Entries;
using Microsoft.AspNetCore.Mvc;

namespace Verity.CashFlow.Entries.Api.Endpoints;

public static class EntryEndpoints
{
    public static IEndpointRouteBuilder MapEntryEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/entries").WithTags("Entries");

        group.MapPost("/", CreateEntry)
            .WithName("CreateEntry")
            .WithSummary("Register a credit or debit cash flow entry.")
            .WithDescription(
                "Persists the entry and stores an integration event for the consolidation "
                + "service. Returns 201 even if the message broker is unavailable.")
            .Produces<EntryResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/{id:guid}", GetEntryById)
            .WithName("GetEntryById")
            .WithSummary("Get a single entry by id.")
            .Produces<EntryResponse>()
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapGet("/", ListEntriesByDate)
            .WithName("ListEntriesByDate")
            .WithSummary("List entries of a given date (paged).")
            .Produces<IReadOnlyList<EntryResponse>>()
            .ProducesValidationProblem();

        return app;
    }

    private static async Task<IResult> CreateEntry(
        CreateEntryRequest request,
        CreateEntryUseCase createEntry,
        CancellationToken cancellationToken)
    {
        var result = await createEntry.ExecuteAsync(
            request.Amount,
            request.Type,
            request.Description,
            request.OccurredAtUtc,
            cancellationToken);

        return result.IsSuccess
            ? Results.Created($"/api/entries/{result.Value.Id}",
                EntryResponse.From(result.Value))
            : Results.Problem(
                title: "Entry validation failed.",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status400BadRequest,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = result.Error.Code
                });
    }

    private static async Task<IResult> GetEntryById(
        Guid id,
        GetEntryByIdUseCase getEntryById,
        CancellationToken cancellationToken)
    {
        var result = await getEntryById.ExecuteAsync(id, cancellationToken);

        return result.IsSuccess
            ? Results.Ok(EntryResponse.From(result.Value))
            : Results.Problem(
                title: "Entry not found.",
                detail: result.Error.Message,
                statusCode: StatusCodes.Status404NotFound,
                extensions: new Dictionary<string, object?>
                {
                    ["errorCode"] = result.Error.Code
                });
    }

    private static async Task<IResult> ListEntriesByDate(
        DateOnly? date,
        int? page,
        int? pageSize,
        ListEntriesByDateUseCase listEntriesByDate,
        CancellationToken cancellationToken)
    {
        var errors = new Dictionary<string, string[]>();

        if (date is null)
            errors["date"] = ["Query parameter 'date' (yyyy-MM-dd) is required."];

        var currentPage = page ?? 1;
        var currentPageSize = pageSize ?? 20;

        if (currentPage < 1)
            errors["page"] = ["Page must be at least 1."];

        if (currentPageSize < 1 || currentPageSize > 100)
            errors["pageSize"] = ["Page size must be between 1 and 100."];

        if (errors.Count > 0)
            return Results.ValidationProblem(errors);

        var entries = await listEntriesByDate.ExecuteAsync(
            date!.Value, currentPage, currentPageSize, cancellationToken);

        return Results.Ok(entries.Select(EntryResponse.From).ToList());
    }
}
