using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace XDrishti.Api.Auth;

/// <summary>
/// Authenticates the bearer token of the tool server (BK-12b): SHA-256 of both sides compared in fixed time, whatever the outcome. A missing, empty or wrong token, or an unset configured token, is "no result" (never a hint which); the challenge is left to the session scheme, so the 401 is the same
/// problem as for any anonymous request. The principal carries the service claim only: no owner, no session, no audit row.
/// </summary>
internal sealed class ServiceReadAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory loggerFactory,
    UrlEncoder encoder,
    IOptions<ServiceTokenOptions> tokens) : AuthenticationHandler<AuthenticationSchemeOptions>(options, loggerFactory, encoder)
{
    private const string Prefix = "Bearer ";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var header = Request.Headers.Authorization.ToString();
        var provided = header.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase) ? header[Prefix.Length..].Trim() : string.Empty;
        var configured = tokens.Value.Mcp;
        var same = CryptographicOperations.FixedTimeEquals(SHA256.HashData(Encoding.UTF8.GetBytes(provided)), SHA256.HashData(Encoding.UTF8.GetBytes(configured ?? string.Empty)));
        if (!same || provided.Length == 0 || string.IsNullOrEmpty(configured))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var identity = new ClaimsIdentity([new Claim(ServiceReadAuthentication.ServiceClaim, ServiceReadAuthentication.McpService)], Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
    }

    /// <summary>The session scheme writes the 401 (it is listed first in the policy); a second writer would append a second body.</summary>
    protected override Task HandleChallengeAsync(AuthenticationProperties properties) => Task.CompletedTask;
}
