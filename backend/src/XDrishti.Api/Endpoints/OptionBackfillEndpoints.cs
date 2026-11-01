using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.OptionBackfill;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's controls of the expired-options backfill (spec P14-03): read the switch, the probe acceptance and the progress; change the switch; accept the probe result. Each change needs a session, the CSRF token and a fresh password for THIS change, on
/// and off alike (ADR 0091); the actor is built from the signed-in identity, never from the body. The routes carry no bar: nothing here reads a recorded row, and nothing reads them anywhere else.
/// </summary>
internal sealed class OptionBackfillEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/options").WithTags("Options");

        group.MapGet("/backfill", GetAsync)
            .WithName("GetOptionBackfill")
            .WithSummary("The owner's expired-options backfill switch (OFF = never set), whether the probe result was accepted, its history, the progress (keys done, empty, given up, left, estimated hours) and why the backfill is not running now. No bars");

        group.MapPut("/backfill", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeOptionBackfill")
            .WithSummary("Switch the expired-options backfill ON or OFF as the owner; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY change (an earlier step-up window does not count and is not used up). It only allows the backfill: the deployment flag Options__Backfill__Enabled, the probe acceptance and the service options-backfill must be on too. 401 auth.invalid_credentials, 423 auth.locked, 403 options.owner_only, 400 options.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPut("/backfill/probe-acceptance", AcceptProbeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("AcceptOptionBackfillProbe")
            .WithSummary("Record, as the owner, that the probe result of the backfill was read and accepted (ADR 0177); the backfill refuses to run until this exists. Appends an audited row. Needs a session, the CSRF token and the password in the body, checked on every call. 401 auth.invalid_credentials, 423 auth.locked, 403 options.owner_only, 400 options.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);
    }

    private static async Task<Ok<OptionBackfillView>> GetAsync(IQueryHandler<GetOptionBackfillQuery, OptionBackfillView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetOptionBackfillQuery(), cancellationToken));

    private static async Task<Results<Ok<OptionBackfillSwitched>, ProblemHttpResult>> ChangeAsync(
        OptionBackfillSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeOptionBackfillCommand, OptionBackfillSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // A fresh password for this change (OwnerAct: no step-up window opened, extended or used up), then the owner from the session; nothing in the body can name the actor.
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeOptionBackfillCommand(owner.Value, request.Enabled, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<BackfillProbeAccepted>, ProblemHttpResult>> AcceptProbeAsync(
        OptionBackfillProbeRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<AcceptBackfillProbeCommand, BackfillProbeAccepted> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new AcceptBackfillProbeCommand(owner.Value, request.Note ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
