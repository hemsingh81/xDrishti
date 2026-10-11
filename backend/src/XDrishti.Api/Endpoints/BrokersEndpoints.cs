using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Accounts;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Accounts.Connections;

namespace XDrishti.Api.Endpoints;

/// <summary>The Dhan redirect URL to register with the Dhan app, and the callback Dhan sends the owner's browser back to after the consent login.</summary>
internal sealed class BrokersEndpoints : IEndpointModule
{
    /// <summary>The SPA page the callback ends on.</summary>
    internal const string AccountsPage = "/accounts";

    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/brokers").WithTags("Brokers");

        group.MapGet("/dhan", GetDhanAsync)
            .WithName("GetDhan")
            .WithSummary("The redirect URL to register with the Dhan app: {public base URL}/api/brokers/dhan/callback");

        // A browser navigation from Dhan's site (top-level GET; the session cookie is SameSite=Lax). It checks the session itself so that
        // it can always answer with a redirect to the SPA instead of a JSON 401; GET needs no CSRF token, and each sign-in is usable once.
        group.MapGet("/dhan/callback", DhanCallbackAsync)
            .AllowAnonymous()
            .WithName("DhanCallback")
            .WithSummary("Dhan's redirect after the consent login (?tokenId=…): exchanges it for a token and redirects (302) to /accounts?account={id}&connected=1 or &connectError={code}")
            .Produces(StatusCodes.Status302Found);
    }

    /// <summary>The SPA address for the outcome of a sign-in: <c>/accounts?account={id}&amp;connected=1</c> or <c>…&amp;connectError={code}</c>.</summary>
    internal static string ReturnUrl(string publicBaseUrl, ConnectReturn outcome)
    {
        var query = new List<string>(2);
        if (outcome.AccountId is { } id)
        {
            query.Add("account=" + id.ToString("D", CultureInfo.InvariantCulture));
        }

        query.Add(outcome.Connected ? "connected=1" : "connectError=" + Uri.EscapeDataString(outcome.ErrorCode!));
        return publicBaseUrl.TrimEnd('/') + AccountsPage + "?" + string.Join('&', query);
    }

    private static async Task<Ok<DhanRedirectView>> GetDhanAsync(
        IQueryHandler<GetDhanRedirectQuery, DhanRedirectView> handler,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetDhanRedirectQuery(), cancellationToken));

    private static async Task<RedirectHttpResult> DhanCallbackAsync(
        [FromQuery] string? tokenId,
        HttpContext http,
        ICommandHandler<CompleteConnectCommand, ConnectReturn> handler,
        IBrokerSettings settings,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new CompleteConnectCommand(http.GetVerifiedSession()?.SessionId, tokenId, http.GetActor()), cancellationToken);
        var outcome = result.IsSuccess ? result.Value : new ConnectReturn(null, result.Error.Code);

        // The callback URL carries a single-use tokenId: never cache this answer or pass the URL on as a referrer.
        http.Response.Headers.CacheControl = "no-store";
        http.Response.Headers["Referrer-Policy"] = "no-referrer";
        return TypedResults.Redirect(ReturnUrl(settings.PublicBaseUrl, outcome));
    }
}
