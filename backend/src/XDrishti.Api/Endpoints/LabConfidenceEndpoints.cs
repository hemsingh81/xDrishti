using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Api.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Lab;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The pre-registration, the locked hold-out and its one-time unlock (spec CF-06, ADR 0207). The registration is the owner's session and CSRF; the unlock is an owner act with a fresh password (verified without opening the step-up window, as P7-12; it queues a Lab job and answers 202), is never cached
/// and cannot be repeated by the same version.
/// </summary>
internal sealed class LabConfidenceEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        api.MapPut("/lab/strategies/{id}/versions/{version:int}/registration", RegisterAsync)
            .WithTags("Lab")
            .WithName("RegisterStrategyVersion")
            .WithSummary("Pre-register a strategy version (owner session): who is on the other side and why they lose, the expected sign of each parameter, tiers and sessions, the expected gross edge and the cost it must beat, and the falsifier. Plain text, five parts, each up to 2,000 characters. 404 lab.not_found, 409 lab.registration_frozen (a registration is written once, and not after a full run froze the plan of the version), 422 lab.invalid_input")
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        api.MapGet("/lab/strategies/{id}/versions/{version:int}/registration", GetRegistrationAsync)
            .WithTags("Lab")
            .WithName("GetStrategyRegistration")
            .WithSummary("The pre-registration of a strategy version. 404 lab.not_found when there is none")
            .ProducesProblem(StatusCodes.Status404NotFound);

        api.MapGet("/lab/holdout", GetHoldOutAsync)
            .WithTags("Lab")
            .WithName("GetLabHoldOut")
            .WithSummary("The locked hold-out: its start and length (or what the policy would give now), whether the Lab enforces it, and every look taken. Numbers appear only in a look that was taken");

        api.MapPost("/lab/runs/{id:guid}/holdout-unlock", UnlockAsync)
            .WithTags("Lab")
            .WithName("UnlockLabHoldOut")
            .WithSummary("Owner act, fresh password in the body: queue the one look of this run's strategy version at the hold-out (a Lab job of method holdout, answered 202 with the job; poll GET /api/lab/runs/{id} until it is succeeded, then read the stored look in the report). The password is checked here and is never stored in the job. Irreversible. 401 auth.invalid_credentials for a wrong password, 423 auth.locked while the sign-in is locked, 409 lab.holdout_used for a second look by the version (the stored result is unchanged; also the failure code of a job that lost the race), 409 lab.no_report, 409 lab.holdout_unavailable, 404 lab.not_found")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked);
    }

    private static async Task<Results<Ok<RegistrationView>, ProblemHttpResult>> RegisterAsync(
        string id,
        int version,
        RegistrationRequest request,
        HttpContext http,
        ICommandHandler<RegisterStrategyCommand, RegistrationView> handler,
        CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RegisterStrategyCommand(id, version, request.Counterparty, request.ParameterSigns, request.Applicability, request.ExpectedEdge, request.Falsifier, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<RegistrationView>, ProblemHttpResult>> GetRegistrationAsync(
        string id,
        int version,
        HttpContext http,
        IQueryHandler<GetRegistrationQuery, Result<RegistrationView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetRegistrationQuery(id, version), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<HoldOutView>, ProblemHttpResult>> GetHoldOutAsync(
        HttpContext http,
        IQueryHandler<GetHoldOutQuery, Result<HoldOutView>> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetHoldOutQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<LabRunView>, ProblemHttpResult>> UnlockAsync(
        Guid id,
        HoldOutUnlockRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<UnlockHoldOutCommand, LabRunView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new UnlockHoldOutCommand(id, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted($"/api/lab/runs/{result.Value.Id}", result.Value);
    }

    /// <summary>The refusals the Lab answers with 422; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        LabErrors.IsUnprocessable(error.Code)
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
