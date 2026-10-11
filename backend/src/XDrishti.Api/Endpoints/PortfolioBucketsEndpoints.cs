using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.Buckets;

namespace XDrishti.Api.Endpoints;

/// <summary>Portfolio buckets (named groups of holdings). Part of the accounts module.</summary>
internal sealed class PortfolioBucketsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/portfolio-buckets").WithTags("Portfolio buckets");

        group.MapGet("/", ListAsync)
            .WithName("ListPortfolioBuckets")
            .WithSummary("All portfolio buckets (seeded: Trading, MyLongTerm, MyChoice)");

        group.MapPost("/", AddAsync)
            .WithName("AddPortfolioBucket")
            .WithSummary("Add a portfolio bucket")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Ok<IReadOnlyList<BucketView>>> ListAsync(
        IQueryHandler<ListBucketsQuery, IReadOnlyList<BucketView>> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListBucketsQuery(), cancellationToken));

    private static async Task<Results<Created<BucketView>, ProblemHttpResult>> AddAsync(
        BucketInput input,
        HttpContext http,
        ICommandHandler<AddBucketCommand, BucketView> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new AddBucketCommand(input, http.GetActor()), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Created($"/api/portfolio-buckets/{result.Value.Id}", result.Value);
    }
}
