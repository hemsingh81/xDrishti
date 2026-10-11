using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Auth;

/// <summary>
/// Authenticates a request from the opaque session cookie by asking the Application layer to verify it
/// (existence, revocation, idle and absolute expiry, throttled last-seen update). Challenges with a 401 problem, never a redirect.
/// </summary>
internal sealed class SessionAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Cookies.TryGetValue(SessionAuthentication.CookieName, out var token) || string.IsNullOrWhiteSpace(token))
        {
            return AuthenticateResult.NoResult();
        }

        var verify = Context.RequestServices.GetRequiredService<IQueryHandler<VerifySessionQuery, Result<VerifiedSession>>>();
        var result = await verify.HandleAsync(new VerifySessionQuery(token, Context.GetClientInfo()), Context.RequestAborted);
        if (result.IsFailure)
        {
            Context.SetAuthFailureCode(result.Error.Code);
            return AuthenticateResult.Fail(result.Error.Code);
        }

        var session = result.Value;
        Context.SetVerifiedSession(session);

        var identity = new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, session.OwnerId.ToString()),
            new Claim(ClaimTypes.Name, session.Info.User.DisplayName),
            new Claim(ClaimTypes.Email, session.Info.User.Email),
            new Claim(SessionAuthentication.SessionIdClaim, session.SessionId.ToString()),
        ], Scheme.Name);
        return AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        var code = Context.GetAuthFailureCode() ?? AuthErrors.UnauthenticatedCode;
        if (code == AuthErrors.SessionExpiredCode)
        {
            SessionCookie.Clear(Context); // the cookie is useless now
        }

        var title = code == AuthErrors.SessionExpiredCode ? AuthErrors.SessionExpired.Message : AuthErrors.Unauthenticated.Message;
        await ProblemResults.WriteAsync(Context, ProblemResults.Create(StatusCodes.Status401Unauthorized, title, code));
    }
}
