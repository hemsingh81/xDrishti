using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.OptionChain;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner's start switch of the option-chain recorder (spec P14-02): read it, and change it as the owner. The change needs a session, the CSRF token and a fresh password for THIS change, on and off alike (ADR 0091); the actor is built from the
/// signed-in identity, never from the body. The two routes carry no chain data: nothing here reads a recorded row, and nothing reads them anywhere else.
/// </summary>
internal sealed class OptionChainEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/options").WithTags("Options");

        group.MapGet("/chain-recorder", GetAsync)
            .WithName("GetOptionChainRecorder")
            .WithSummary("The owner's option-chain recorder switch (OFF = never set), its history and why the recorder is not running now (deployment flag, switch, service, token, plan, disk). No chain data");

        group.MapPut("/chain-recorder", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeOptionChainRecorder")
            .WithSummary("Switch the option-chain recorder ON or OFF as the owner; appends an audited row. Needs a session, the CSRF token and the password in the body, checked on EVERY change (an earlier step-up window does not count and is not used up). It only allows the recorder: the deployment flag Options__Chain__Enabled and the service option-chain-recorder must be on too. 401 auth.invalid_credentials, 423 auth.locked, 403 options.owner_only, 400 options.reason_invalid, 429 auth.rate_limited")
            .ProducesRejections(conflict: false, notFound: false);
    }

    private static async Task<Ok<OptionChainRecorderView>> GetAsync(IQueryHandler<GetOptionChainRecorderQuery, OptionChainRecorderView> handler, CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetOptionChainRecorderQuery(), cancellationToken));

    private static async Task<Results<Ok<OptionChainRecorderSwitched>, ProblemHttpResult>> ChangeAsync(
        OptionChainSwitchRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeOptionChainRecorderCommand, OptionChainRecorderSwitched> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";

        // A fresh password for this change (OwnerAct: no step-up window opened, extended or used up), then the owner from the session; nothing in the body can name the actor.
        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeOptionChainRecorderCommand(owner.Value, request.Enabled, request.Reason ?? string.Empty), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
