using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.OpeningBars;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's switch of the opening-minute recorder (spec BK-03b): read it, and change it as the owner. The change needs a session, the CSRF token and a fresh password for THIS change, on and off alike (ADR 0091); the actor is built from the signed-in
/// identity, never from the body. The two routes carry no bar: nothing here reads a recorded row.
/// </summary>
internal sealed class OpeningBarsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/opening-bars").WithTags("OpeningBars");

        group.MapGet("/recorder", GetAsync)
            .WithName("GetOpeningBarsRecorder")
            .WithSummary("The owner's opening-minute recorder switch (OFF = never set), its history and why the recorder is not running now (deployment flag, switch, service). No bar data");

        group.MapPut("/recorder", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeOpeningBarsRecorder")
            .WithSummary("Switch the opening-minute recorder ON or OFF as the owner; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY change. It only allows the recorder: the deployment flag OpeningBars__Enabled and the service opening-bars must be on too. 401 auth.invalid_credentials, 423 auth.locked, 403 opening_bars.owner_only, 400 opening_bars.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);
    }

    private static async Task<Ok<OpeningBarsRecorderView>> GetAsync(IQueryHandler<GetOpeningBarsRecorderQuery, OpeningBarsRecorderView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetOpeningBarsRecorderQuery(), cancellationToken));

    private static async Task<Results<Ok<OpeningBarsSwitched>, ProblemHttpResult>> ChangeAsync(
        OpeningBarsSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeOpeningBarsCommand, OpeningBarsSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // A fresh password for this change (OwnerAct: no step-up window opened, extended or used up), then the owner from the session; nothing in the body can name the actor.
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeOpeningBarsCommand(owner.Value, request.Enabled, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
