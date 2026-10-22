using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Api.Lab;
using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>The Strategy Lab API (spec P5-01, ADR 0057): validate a strategy text, store it as an immutable version, read the library, download and clone. Never runs anything.</summary>
internal sealed class LabEndpoints : IEndpointModule
{
    /// <summary>Transport limit of a body; the text limit (64 KB) is judged by the handler with a located problem.</summary>
    private const long MaxBodyBytes = 256 * 1024;

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/lab").WithTags("Lab");

        group.MapPost("/validate", ValidateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .WithName("ValidateStrategy")
            .WithSummary("Read and compile a strategy text; the answer is valid (no error), the hash and the located problems. An invalid text is still 200. Writes nothing")
            .ProducesProblem(StatusCodes.Status400BadRequest);

        group.MapGet("/schema", GetSchemaAsync)
            .WithName("GetStrategySchema")
            .WithSummary("The JSON Schema (draft 2020-12) of the strategy file, for the editor")
            .Produces<string>(StatusCodes.Status200OK, "application/schema+json");

        var strategies = group.MapGroup("/strategies");

        strategies.MapGet("/", ListAsync)
            .WithName("ListStrategies")
            .WithSummary("The strategy families with their latest version number, the number of versions and whether they are built in");

        strategies.MapPost("/", CreateAsync)
            .WithMetadata(new RequestSizeLimitAttribute(MaxBodyBytes))
            .WithName("CreateStrategy")
            .WithSummary("Store a valid text as a new family (201) or as the next version of one (201); the same content again is the same version (200, nothing written). 422 lab.invalid_strategy with the problems; 409 lab.version_exists when the text's version is not the next number, lab.builtin_readonly for a built-in")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        strategies.MapGet("/{id}", GetAsync)
            .WithName("GetStrategy")
            .WithSummary("One strategy family with the list of its versions")
            .ProducesProblem(StatusCodes.Status404NotFound);

        strategies.MapGet("/{id}/versions/{version:int}", GetVersionAsync)
            .WithName("GetStrategyVersion")
            .WithSummary("One stored version: the text as posted, hash, parent hash, note and whether the text is free of errors under today's rules")
            .ProducesProblem(StatusCodes.Status404NotFound);

        strategies.MapGet("/{id}/versions/{version:int}/file", DownloadAsync)
            .WithName("DownloadStrategyVersion")
            .WithSummary("The text of a stored version as a YAML file (attachment); posted to validate it gives the same hash")
            .Produces<string>(StatusCodes.Status200OK, "application/yaml")
            .ProducesProblem(StatusCodes.Status404NotFound);

        strategies.MapPost("/{id}/versions/{version:int}/clone", CloneAsync)
            .WithName("CloneStrategyVersion")
            .WithSummary("The text of a stored version with a new id (a built-in gets <ID>_COPY) and the parent hash to post with it; stores nothing")
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict);
    }

    private static async Task<Results<Ok<StrategyValidation>, ProblemHttpResult>> ValidateAsync(
        ValidateStrategyRequest request,
        HttpContext http,
        IQueryHandler<ValidateStrategyQuery, StrategyValidation> handler,
        CancellationToken cancellationToken)
    {
        if (request.Format is not null && !string.Equals(request.Format, "yaml", StringComparison.OrdinalIgnoreCase))
        {
            return ProblemResults.From(Error.Validation("lab.unsupported_format", "Only `yaml` is accepted."), http);
        }

        return TypedResults.Ok(await handler.HandleAsync(new ValidateStrategyQuery(request.Text ?? string.Empty), cancellationToken));
    }

    private static async Task<ContentHttpResult> GetSchemaAsync(IQueryHandler<GetStrategySchemaQuery, string> handler, CancellationToken cancellationToken) =>
        TypedResults.Text(await handler.HandleAsync(new GetStrategySchemaQuery(), cancellationToken), "application/schema+json", Encoding.UTF8);

    private static async Task<Ok<IReadOnlyList<StoredStrategy>>> ListAsync(IQueryHandler<ListStrategiesQuery, IReadOnlyList<StoredStrategy>> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new ListStrategiesQuery(), cancellationToken));

    private static async Task<Results<Created<CreatedStrategy>, Ok<CreatedStrategy>, ProblemHttpResult>> CreateAsync(
        CreateStrategyRequest request,
        HttpContext http,
        ICommandHandler<CreateStrategyCommand, CreatedStrategy> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CreateStrategyCommand(request.Text ?? string.Empty, request.Note, request.ParentHash), cancellationToken);
        if (result.IsFailure)
        {
            return Problem(result.Error, http);
        }

        var created = result.Value;
        return created.Created
            ? TypedResults.Created($"/api/lab/strategies/{created.Id}/versions/{created.Version}", created)
            : TypedResults.Ok(created);
    }

    private static async Task<Results<Ok<StrategyDetail>, ProblemHttpResult>> GetAsync(
        string id,
        HttpContext http,
        IQueryHandler<GetStrategyQuery, Result<StrategyDetail>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetStrategyQuery(id), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<StrategyVersionView>, ProblemHttpResult>> GetVersionAsync(
        string id,
        int version,
        HttpContext http,
        IQueryHandler<GetStrategyVersionQuery, Result<StrategyVersionView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetStrategyVersionQuery(id, version), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<ContentHttpResult, ProblemHttpResult>> DownloadAsync(
        string id,
        int version,
        HttpContext http,
        IQueryHandler<GetStrategyVersionQuery, Result<StrategyVersionView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetStrategyVersionQuery(id, version), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        http.Response.Headers.ContentDisposition = $"attachment; filename=\"{result.Value.Id}-v{result.Value.Version}.yaml\"";
        return TypedResults.Text(result.Value.Text, "application/yaml", Encoding.UTF8);
    }

    private static async Task<Results<Ok<ClonedStrategy>, ProblemHttpResult>> CloneAsync(
        string id,
        int version,
        CloneStrategyRequest? request,
        HttpContext http,
        IQueryHandler<CloneStrategyQuery, Result<ClonedStrategy>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CloneStrategyQuery(id, version, request?.NewId), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused text is 422 with the located problems; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http)
    {
        if (error.Code != LabErrors.InvalidStrategyCode || error.Payload is not { } payload || !payload.TryGetValue(LabErrors.ProblemsKey, out var problems))
        {
            return ProblemResults.From(error, http);
        }

        var details = ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code);
        details.Extensions[LabErrors.ProblemsKey] = (IReadOnlyList<StrategyIssue>)problems;
        return TypedResults.Problem(details);
    }
}
