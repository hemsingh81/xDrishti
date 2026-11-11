using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;

namespace XDrishti.Api.Endpoints;

/// <summary>The body of a change of the universe settings; a value left out stays.</summary>
internal sealed record UniverseSettingsChangeBody(int? MinTradesChild, int? LinkMinTrades, string? RegisteredBaskets, string? Password);

/// <summary>
/// The owner's universe settings (BK-11, ADR 0261): reading is open to the session; every change needs the session, the CSRF token and a fresh password for THIS change, and the actor comes from the session. Nothing here applies a restriction, changes a scan scope or places an order.
/// </summary>
internal sealed class UniverseSettingsEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/universe").WithTags("Learning");

        group.MapGet("/settings", GetAsync)
            .WithName("GetUniverseSettings")
            .WithSummary("The universe settings in force (the fewest trades of a tier cell to be read in place of its parent, 30; the fewest trades of a tier to be an observation, 100; the registered basket names a strategy x basket comparison may use) and the latest 50 changes. Read only");

        group.MapPut("/settings", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithName("ChangeUniverseSettings")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status403Forbidden)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status423Locked)
            .WithSummary("The owner changes the universe settings (minTradesChild 1 to 1000, linkMinTrades 10 to 10000, registeredBaskets: comma separated names, 1 to 20); a value left out stays. The password is needed on EVERY change (401 auth.invalid_credentials, 423 auth.locked); each key that changes is appended as a row. 403 learning.owner_only, 422 learning.invalid");
    }

    private static async Task<Ok<UniverseSettingsView>> GetAsync(IQueryHandler<GetUniverseSettingsQuery, UniverseSettingsView> handler, HttpContext http, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(await handler.HandleAsync(new GetUniverseSettingsQuery(), cancellationToken));
    }

    private static async Task<Results<Ok<UniverseSettingsView>, ProblemHttpResult>> ChangeAsync(UniverseSettingsChangeBody request, HttpContext http, ICommandHandler<ChangeUniverseSettingsCommand, UniverseSettingsView> handler, CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var session = http.GetVerifiedSession();
        var owner = OwnerAct.SessionOwner(http);
        if (owner.IsFailure || session is null)
        {
            return ProblemResults.From(owner.IsFailure ? owner.Error : AuthErrors.Unauthenticated, http);
        }

        var result = await handler.HandleAsync(new ChangeUniverseSettingsCommand(owner.Value, session.SessionId, http.GetClientInfo(), request.MinTradesChild, request.LinkMinTrades, request.RegisteredBaskets, request.Password), cancellationToken);
        return result.IsSuccess
            ? TypedResults.Ok(result.Value)
            : result.Error.Code == ChangeUniverseSettingsHandlerCodes.Invalid
                ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, result.Error.Message, result.Error.Code))
                : ProblemResults.From(result.Error, http);
    }
}

/// <summary>The codes the endpoint maps; the handler is internal.</summary>
internal static class ChangeUniverseSettingsHandlerCodes
{
    public const string Invalid = "learning.invalid";
}
