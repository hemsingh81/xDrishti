using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Reporting;

namespace XDrishti.Api.Endpoints;

/// <summary>The one write of the basket reports (BK-06c): the owner's choice of the index the basket profile and ranking compare with. Kept apart from the read-only <see cref="ReportsEndpoints"/>, which maps GET and nothing else.</summary>
internal sealed class BasketBenchmarkEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapGroup("/reports").WithTags("Reports").MapPut("/basket-benchmark", SetAsync)
            .WithName("SetBasketBenchmark")
            .WithSummary("Choose the index the basket profile and ranking compare with (default Nifty 500): body {instrumentId} of a listed index of the instrument master. Session and CSRF token, no password (it loosens nothing). Answers the index {id, symbol, isDefault, name}. 422 reports.invalid for no id, an unknown id, a non-index or a retired index. Audited; the portfolio benchmark is not changed.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);
    }

    private static async Task<Results<Ok<ProfileIndexView>, ProblemHttpResult>> SetAsync(
        SetBasketBenchmarkInput input,
        HttpContext http,
        ICommandHandler<SetBasketBenchmarkCommand, ProfileIndexView> handler,
        CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http); // a session is enough: the choice loosens nothing
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SetBasketBenchmarkCommand(input.InstrumentId, owner.Value), cancellationToken);
        if (result.IsFailure)
        {
            return result.Error.Kind == Domain.Common.ErrorKind.Validation
                ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
                : ProblemResults.From(result.Error, http);
        }

        return TypedResults.Ok(result.Value);
    }
}
