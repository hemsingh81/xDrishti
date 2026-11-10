using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.SettingsCatalog;

namespace XDrishti.Api.Endpoints;

/// <summary>The body of a change to the Failure notices setting: on or off, and the password (needed to turn it OFF).</summary>
internal sealed record ChangeEssentialRequest(bool On, string? Password);

/// <summary>The body of a reset: the owner's password, re-entered for THIS act.</summary>
internal sealed record ResetEssentialRequest(string? Password);

/// <summary>
/// The Essential settings with who changed each and when (AU-10, ADR 0242): read them, change Failure notices, and Reset to default. A reset and turning Failure notices OFF need a fresh password for that act (checked on every request); the actor is built from the signed-in session.
/// Nothing here places an order.
/// </summary>
internal sealed class EssentialSettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/settings/essentials").WithTags("Settings");

        group.MapGet("/", GetAsync)
            .WithName("GetEssentialSettings")
            .WithSummary("The five Essential settings with value, default, whether it is the default, who changed it and when, and whether Reset to default works here (risk.profile has no single default)");

        group.MapPut("/{key}", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ChangeEssentialSetting")
            .WithSummary("Turn Failure notices (notices.failure) or Auto (auto.enabled) on or off as the owner. Turning Failure notices OFF, or Auto ON, needs the password in the body (403 auth.step_up_required, 401 auth.invalid_credentials, 423 auth.locked). 409 settings.not_here for any other key")
            .ProducesRejections(conflict: true);

        group.MapPost("/{key}/reset", ResetAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ResetEssentialSetting")
            .WithSummary("Put an Essential setting back to its default as the owner, with a fresh password for this act. 404 settings.not_found, 409 settings.reset_not_single for risk.profile")
            .ProducesRejections(conflict: true);
    }

    private static async Task<Ok<EssentialSettingsView>> GetAsync(IQueryHandler<GetEssentialSettingsQuery, EssentialSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetEssentialSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<EssentialSettingView>, ProblemHttpResult>> ChangeAsync(
        string key,
        ChangeEssentialRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeEssentialSettingCommand, EssentialSettingView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var fresh = key == "auto.enabled" ? request.On : !request.On; // Auto: ON needs the password, OFF a session (the safe direction); Failure notices: the other way round
        var owner = fresh ? await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken) : OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeEssentialSettingCommand(key, request.On, owner.Value, fresh), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }

    private static async Task<Results<Ok<EssentialSettingView>, ProblemHttpResult>> ResetAsync(
        string key,
        ResetEssentialRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ResetEssentialSettingCommand, EssentialSettingView> handler,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var owner = await OwnerAct.FreshAsync(http, passwordCheck, request.Password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ResetEssentialSettingCommand(key, owner.Value), cancellationToken);
        return result.IsFailure ? ProblemResults.From(result.Error, http) : TypedResults.Ok(result.Value);
    }
}
