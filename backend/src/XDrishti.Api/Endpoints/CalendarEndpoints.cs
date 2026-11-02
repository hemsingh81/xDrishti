using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Filings;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The results calendar (spec P15-04): the results meetings visible at an instant, with the tracked instruments that have none. Read-only; no file content, no estimate of any date.
/// </summary>
internal sealed class CalendarEndpoints : IEndpointModule
{
    private static readonly string[] Known = ["from", "to", "asOf"];

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/calendar").WithTags("Calendar");

        group.MapGet("/results", GetResultsAsync)
            .WithName("GetResultsCalendar")
            .WithSummary("The results meetings known at asOf (default now; an instant with an offset) whose event session (the first trading day on or after the meeting date) lies between from and to (default: today in India and the next 59 days; at most 120 days): symbol, period end, meeting date, event session, trading days from the India date of asOf, when the exchange published the row, revision (1, then 2 and up for reschedules), plus the count and the first 300 symbols of the tracked instruments without an entry. A meeting published after asOf is not shown. An empty list is not an error; 400 calendar.invalid_query for a bad range, an asOf without an offset or an unknown parameter")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<ResultsCalendarView>, ProblemHttpResult>> GetResultsAsync(
        HttpContext http,
        IQueryHandler<GetResultsCalendarQuery, Result<ResultsCalendarView>> handler,
        CancellationToken cancellationToken,
        string? from = null,
        string? to = null,
        string? asOf = null)
    {
        var unknown = http.Request.Query.Keys.Any(k => !Known.Contains(k, StringComparer.Ordinal));
        var result = await handler.HandleAsync(new GetResultsCalendarQuery(from, to, asOf, unknown), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
