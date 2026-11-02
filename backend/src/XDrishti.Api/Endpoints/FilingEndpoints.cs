using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The stored filings as metadata and counts (spec P15-02): coverage per kind and quarter, the list of envelopes (optionally as of an instant, by the point-in-time rule) and one envelope. Read-only; no file content, no path, no source reference.
/// </summary>
internal sealed class FilingEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/filings").WithTags("Filings");

        group.MapGet("/coverage", GetCoverageAsync)
            .WithName("GetFilingCoverage")
            .WithSummary("Counts of the stored filings between from and to (default: the last 365 days; at most 800) per kind and quarter: stored with a broadcast time, stored without one (by reason), superseded, out of order, and the tracked instruments with none. A kind with nothing stored answers unavailable. Counts only; 400 filings.invalid_query for a bad range")
            .ProducesValidationProblem();

        group.MapGet("", ListAsync)
            .WithName("ListFilings")
            .WithSummary("Filing envelope metadata, newest broadcast first, at most 200 rows (no file content, no path). With asOf (an instant with an offset, needs instrument and kind) it is the point-in-time answer: the filings the market knew at that instant. 400 filings.invalid_query for an unknown kind or instrument or an asOf without an offset")
            .ProducesValidationProblem();

        group.MapGet("/{id:guid}", GetAsync)
            .WithName("GetFiling")
            .WithSummary("The metadata of one stored filing; 404 filings.not_found for an unknown id")
            .ProducesProblem(StatusCodes.Status404NotFound);
    }

    private static async Task<Results<Ok<FilingCoverageView>, ProblemHttpResult>> GetCoverageAsync(
        HttpContext http,
        IQueryHandler<GetFilingCoverageQuery, Result<FilingCoverageView>> handler,
        CancellationToken cancellationToken,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var result = await handler.HandleAsync(new GetFilingCoverageQuery(from, to), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<FilingListView>, ProblemHttpResult>> ListAsync(
        HttpContext http,
        IQueryHandler<ListFilingsQuery, Result<FilingListView>> handler,
        CancellationToken cancellationToken,
        string? instrument = null,
        string? kind = null,
        string? asOf = null)
    {
        var result = await handler.HandleAsync(new ListFilingsQuery(instrument, kind, asOf), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<FilingView>, ProblemHttpResult>> GetAsync(
        HttpContext http,
        Guid id,
        IQueryHandler<GetFilingQuery, Result<FilingView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetFilingQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
