using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Domain.Checklist;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// What every owner act of the web checks before it calls its handler (spec P7-12, P8-23): there is a verified session, and the owner typed the password again for THIS act. The password is verified with the sign-in lockout rules and audit, without opening, extending or using up the
/// 5-minute step-up window (an earlier window does not count). The actor is built from the signed-in identity, never from a body; with no e-mail address there is no owner and the handler refuses an empty actor.
/// </summary>
internal static class OwnerAct
{
    /// <summary>The owner actor (<c>owner:&lt;e-mail&gt;</c>) after a fresh password, or the problem to answer with.</summary>
    public static async Task<Result<string>> AuthorizeAsync(HttpContext http, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, string? password, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(passwordCheck);
        var session = http.GetVerifiedSession();
        if (session is null)
        {
            return Result.Failure<string>(AuthErrors.Unauthenticated);
        }

        var confirmed = await passwordCheck.HandleAsync(new StepUpCommand(session.SessionId, password, http.GetClientInfo(), Elevate: false), cancellationToken);
        if (confirmed.IsFailure)
        {
            return Result.Failure<string>(confirmed.Error);
        }

        var email = session.Info.User.Email;
        return Result.Success(string.IsNullOrWhiteSpace(email) ? string.Empty : OwnerConfirmation.OwnerPrefix + email);
    }

    /// <summary>
    /// The owner actor of the signed-in session WITHOUT a password: only for the safe direction (switching a capability OFF, cancelling a job), where being slow would be the danger. Needs the verified session (and, by the CSRF middleware, the token).
    /// With no e-mail address there is no owner and the handler refuses an empty actor.
    /// </summary>
    public static Result<string> SessionOwner(HttpContext http)
    {
        ArgumentNullException.ThrowIfNull(http);
        var session = http.GetVerifiedSession();
        if (session is null)
        {
            return Result.Failure<string>(AuthErrors.Unauthenticated);
        }

        var email = session.Info.User.Email;
        return Result.Success(string.IsNullOrWhiteSpace(email) ? string.Empty : OwnerConfirmation.OwnerPrefix + email);
    }

    /// <summary>
    /// The server-side capability gate of an endpoint (spec P8-18 Part A, ADR 0128), generic by name: the problem to answer with (403 <c>capabilities.off</c>) when the capability is not ON now, else null. Ask it BEFORE the password check, so that a switched-off
    /// act neither counts toward the sign-in lockout nor says anything about the password. The handler behind the endpoint asks the same gate again.
    /// </summary>
    public static async Task<ProblemHttpResult?> RefusedWhileOffAsync(HttpContext http, ICapabilityGate gate, string capability, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(http);
        ArgumentNullException.ThrowIfNull(gate);
        var open = await gate.RequireAsync(capability, cancellationToken);
        return open.IsFailure ? ProblemResults.From(open.Error, http) : null;
    }
}
