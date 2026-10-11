using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;

namespace XDrishti.Api.Endpoints;

internal sealed class AuthEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/auth").WithTags("Auth");

        group.MapPost("/login", LoginAsync)
            .AllowAnonymous()
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy)
            .WithMetadata(CsrfExemptMetadata.Instance)
            .WithName("Login")
            .WithSummary("Sign in with e-mail and password; sets the session cookie")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked)
            .ProducesProblem(StatusCodes.Status429TooManyRequests);

        group.MapPost("/step-up", StepUpAsync)
            .WithName("StepUp")
            .WithSummary("Re-enter the password to allow sensitive actions for 5 minutes")
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status423Locked);

        group.MapGet("/session", GetSession)
            .AllowAnonymous()
            .WithName("GetSession")
            .WithSummary("The current session (user, CSRF token, timeouts), or 401")
            .ProducesProblem(StatusCodes.Status401Unauthorized);

        group.MapPost("/logout", LogoutAsync)
            .WithName("Logout")
            .WithSummary("End the current session and clear the cookie");

        group.MapGet("/verify", Verify)
            .WithName("VerifySession")
            .WithSummary("204 when the cookie holds a valid session (used by the proxy for the static docs)");

        group.MapGet("/audit", GetAuditAsync)
            .WithName("GetAuthAudit")
            .WithSummary("Latest sign-in events, newest first");
    }

    private static async Task<Results<Ok<SessionInfo>, ProblemHttpResult>> LoginAsync(
        LoginRequest request,
        HttpContext http,
        ICommandHandler<LoginCommand, LoginResult> handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(new LoginCommand(request.Email, request.Password, http.GetClientInfo()), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        SessionCookie.Write(http, result.Value.SessionToken, result.Value.Session.ExpiresAt);
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(result.Value.Session);
    }

    private static async Task<Results<Ok<StepUpResult>, ProblemHttpResult>> StepUpAsync(
        StepUpRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> handler,
        CancellationToken cancellationToken)
    {
        // Authorization guarantees a verified session here.
        var session = http.GetVerifiedSession()!;
        var result = await handler.HandleAsync(new StepUpCommand(session.SessionId, request.Password, http.GetClientInfo()), cancellationToken);
        if (result.IsFailure)
        {
            return ProblemResults.From(result.Error, http);
        }

        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(result.Value);
    }

    private static Results<Ok<SessionInfo>, ProblemHttpResult> GetSession(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        if (http.GetVerifiedSession() is { } session)
        {
            return TypedResults.Ok(session.Info);
        }

        if (http.GetAuthFailureCode() == AuthErrors.SessionExpiredCode)
        {
            SessionCookie.Clear(http);
            return ProblemResults.From(AuthErrors.SessionExpired, http);
        }

        return ProblemResults.From(AuthErrors.Unauthenticated, http);
    }

    private static async Task<NoContent> LogoutAsync(
        HttpContext http,
        ICommandHandler<LogoutCommand> handler,
        CancellationToken cancellationToken)
    {
        // Authorization guarantees a verified session here.
        var session = http.GetVerifiedSession()!;
        await handler.HandleAsync(new LogoutCommand(session.SessionId, http.GetClientInfo()), cancellationToken);
        SessionCookie.Clear(http);
        return TypedResults.NoContent();
    }

    private static NoContent Verify(HttpContext http)
    {
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.NoContent();
    }

    private static async Task<Ok<IReadOnlyList<AuditEventResponse>>> GetAuditAsync(
        IQueryHandler<GetAuditQuery, IReadOnlyList<AuditEventResponse>> handler,
        [FromQuery] int? take,
        CancellationToken cancellationToken) =>
        TypedResults.Ok(await handler.HandleAsync(new GetAuditQuery(take ?? GetAuditQuery.DefaultTake), cancellationToken));
}
