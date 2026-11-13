using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Api.Lab;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Replay.HoldOut;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The pre-registered system replay over a hold-out block (SR-03, ADR 0284): the owner stores a registration before the block begins, runs it once with a fresh password after the block seals (or the auto steps do), and reads the verdict with its rule and the hindsight label.
/// Owner session only: the result is research only and no assistant or evidence reader policy is attached. It places no order.
/// </summary>
internal sealed class HoldOutRegistrationEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/replay").WithTags("Replay");

        group.MapPost("/holdout-registrations", RegisterAsync)
            .WithName("RegisterHoldOutReplay")
            .WithSummary("Pre-register the system for the next hold-out block that has not begun (owner session): body basketIds, instrumentIds (empty: the stored Auto scope), capital, riskProfileId, costSheetId (a checked sheet, required), maxDrawdownPct (default 15). The canonical config (versions by hash, the settings snapshot, the resolved scope, the sheet's content hash, both arms, the engine version, the pass rule) is stored with its SHA-256. 200 with id, configHash, blockStart. 422 replay.registration_invalid with a reason: no_future_block, gross_choice, sheet_missing, sheet_unchecked, scope_empty, scope_over_memory_cap, bad_rule.")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity);

        group.MapGet("/holdout-registrations", ListAsync)
            .WithName("ListHoldOutReplayRegistrations")
            .WithSummary("The registrations, newest first, each with its state (waiting, superseded, claimed, run), its rule and, when it ran, the verdict. Research only.");

        group.MapGet("/holdout-registrations/{id:guid}", GetAsync)
            .WithName("GetHoldOutReplayRegistration")
            .WithSummary("One registration: the frozen hash and inputs, the rule first, the state (waiting, superseded, drift, claimed, run) and the verdict with both arms' figures and the hindsight label. Research only. 404 replay.registration_not_found.")
            .ProducesProblem(StatusCodes.Status404NotFound);

        group.MapPost("/holdout-registrations/{id:guid}/run", RunAsync)
            .WithName("RunHoldOutReplayRegistration")
            .WithSummary("Owner act, fresh password in the body: claim the one system look of the registration's block and run both arms over it once (irreversible: the look is burned even if the run is abandoned). 202 with the ids of the two results. 401 auth.invalid_credentials, 403 auth.step_up_required, 423 auth.locked, 409 replay.registration_drift (a stored input is missing or the sheet changed; nothing is claimed), replay.holdout_used (a second run), replay.registration_superseded, lab.holdout_filling, lab.holdout_unavailable, 404 replay.registration_not_found.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .ProducesProblem(StatusCodes.Status423Locked);

        group.MapGet("/settings", GetSettingsAsync)
            .WithName("GetReplayHoldOutSettings")
            .WithSummary("The two automatic switches of the system replay: autoRegister and autoRun (default ON, ADR 0282); null is unavailable.");

        group.MapPut("/settings", PutSettingsAsync)
            .WithName("PutReplayHoldOutSettings")
            .WithSummary("Owner act, fresh password in the body: set autoRegister and/or autoRun (turning either OFF or back ON). Each change is an appended row with the owner as actor. Neither switch ever places an order. 401, 403, 423 as for a run.")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status423Locked);
    }

    private static async Task<Results<Ok<RegisteredHoldOut>, ProblemHttpResult>> RegisterAsync(
        HoldOutRegistrationRequest request,
        HttpContext http,
        ICommandHandler<RegisterHoldOutCommand, RegisteredHoldOut> handler,
        CancellationToken cancellationToken)
    {
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RegisterHoldOutCommand(request.BasketIds, request.InstrumentIds, request.Capital, request.RiskProfileId, request.CostSheetId, request.MaxDrawdownPct, RegisterHoldOutCommand.OwnerMode, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<IReadOnlyList<HoldOutRegistrationView>>, ProblemHttpResult>> ListAsync(HttpContext http, IQueryHandler<ListHoldOutRegistrationsQuery, Result<IReadOnlyList<HoldOutRegistrationView>>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new ListHoldOutRegistrationsQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<HoldOutRegistrationView>, ProblemHttpResult>> GetAsync(Guid id, HttpContext http, IQueryHandler<GetHoldOutRegistrationQuery, Result<HoldOutRegistrationView>> handler, CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new GetHoldOutRegistrationQuery(id), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Accepted<StartedHoldOutRun>, ProblemHttpResult>> RunAsync(
        Guid id,
        HoldOutPasswordRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<RunHoldOutRegistrationCommand, StartedHoldOutRun> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new RunHoldOutRegistrationCommand(id, RegisterHoldOutCommand.OwnerMode, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Accepted("/api/replay/holdout-registrations/" + id.ToString("D"), result.Value);
    }

    private static async Task<Ok<HoldOutAutoSettingsView>> GetSettingsAsync(HttpContext http, IQueryHandler<GetHoldOutAutoSettingsQuery, HoldOutAutoSettingsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetHoldOutAutoSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<HoldOutAutoSettingsView>, ProblemHttpResult>> PutSettingsAsync(
        HoldOutAutoSettingsRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<SetHoldOutAutoSettingsCommand, HoldOutAutoSettingsView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new SetHoldOutAutoSettingsCommand(request.AutoRegister, request.AutoRun, owner.Value), cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused registration is 422; everything else is the shared mapping (409, 404).</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Kind == ErrorKind.Validation
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
