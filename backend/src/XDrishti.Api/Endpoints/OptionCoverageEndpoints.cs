using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.OptionQuality;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The stored quality verdicts of the option archive (spec P14-04): per session and check a status (pass, warn, fail, unavailable), the share measured against its threshold and the reason, the usable flags and the cumulative usable chain sessions. One read-only route; it
/// returns no price, IV or Greek and reads only the result rows. Part of the options module.
/// </summary>
internal sealed class OptionCoverageEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/options").WithTags("Options");

        group.MapGet("/coverage", GetAsync)
            .WithName("GetOptionCoverage")
            .WithSummary("The quality verdicts of one underlying's option archive between from and to (default: the last 30 days; at most 400): per session and check the status (pass|warn|fail|unavailable), value, threshold and reason, whether the session is usable, and the cumulative usable chain sessions up to to. 400 options.invalid_query for an unknown underlying or a bad range; an empty list when nothing is recorded. No price, IV or Greek")
            .ProducesValidationProblem();
    }

    private static async Task<Results<Ok<OptionCoverageView>, ProblemHttpResult>> GetAsync(
        HttpContext http,
        IQueryHandler<GetOptionCoverageQuery, Result<OptionCoverageView>> handler,
        CancellationToken cancellationToken,
        string? underlying = null,
        DateOnly? from = null,
        DateOnly? to = null)
    {
        var result = await handler.HandleAsync(new GetOptionCoverageQuery(underlying, from, to), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
