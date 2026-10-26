using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The capability switches of the owner-act slices (spec P8-18 Part A, ADR 0128): read them, and switch one as the owner. Switching ON needs a session, the CSRF token and the password typed again for THIS act (an earlier step-up window does not count and is not used up);
/// switching OFF needs the session and the CSRF token only, because the kill switch is never slower than the switch. The actor is built from the signed-in identity, never from the body. This module is the only place that writes the capability log; every capability is OFF
/// until the owner switches it on, and no endpoint, job or setting can.
/// </summary>
internal sealed class CapabilitiesEndpoints : IEndpointModule
{
    public void Map(IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/capabilities").WithTags("Capabilities");

        group.MapGet("/", GetAsync)
            .WithName("GetCapabilities")
            .WithSummary("The owner-act capabilities (experiments_ui, learning_moves_ui, autonomy_form, exports, pwa), each OFF until the owner switches it on, with what it allows, when it was last switched and the latest switches. Read only")
            .ProducesRejections(conflict: false, notFound: false);

        group.MapPut("/{name}", ChangeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithName("ChangeCapability")
            .WithSummary("Switch one capability ON or OFF as the owner; appends an audited row (the same state again writes nothing and answers changed=false). Switching ON needs a session, the CSRF token and the password in the body, checked on EVERY request; switching OFF needs no password. 401 auth.invalid_credentials, 423 auth.locked, 404 capabilities.not_found, 422 capabilities.invalid, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: false);
    }

    private static async Task<Ok<CapabilitiesView>> GetAsync(HttpContext http, IQueryHandler<GetCapabilitiesQuery, CapabilitiesView> handler, CancellationToken cancellationToken)
    {
        var view = await handler.HandleAsync(new GetCapabilitiesQuery(), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return TypedResults.Ok(view);
    }

    private static async Task<Results<Ok<CapabilityChangeOutcome>, ProblemHttpResult>> ChangeAsync(
        string name,
        ChangeCapabilityRequest request,
        HttpContext http,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        ICommandHandler<ChangeCapabilityCommand, CapabilityChangeOutcome> handler,
        CancellationToken cancellationToken)
    {
        // A name outside the whitelist is answered before any password is checked: it says nothing about the password.
        if (!Domain.Capabilities.Capabilities.IsKnown(name))
        {
            return ProblemResults.From(Domain.Capabilities.CapabilityErrors.NotFound(name), http);
        }

        // ON: a fresh password for this act. OFF: the signed-in owner is enough (the kill switch is never slower than the switch).
        var owner = request.Enabled
            ? await OwnerAct.AuthorizeAsync(http, passwordCheck, request.Password, cancellationToken)
            : OwnerAct.SessionOwner(http);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var result = await handler.HandleAsync(new ChangeCapabilityCommand(name, request.Enabled, request.Reason ?? string.Empty, owner.Value), cancellationToken);
        http.Response.Headers.CacheControl = "no-store";
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused reason is 422 as in the other owner acts of the learning area; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == Domain.Capabilities.CapabilityErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
