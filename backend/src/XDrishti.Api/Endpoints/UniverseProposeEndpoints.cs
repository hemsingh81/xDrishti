using System.Globalization;
using Microsoft.AspNetCore.Http.HttpResults;
using XDrishti.Api.Auth;
using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Auth;
using XDrishti.Application.Learning;
using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;

namespace XDrishti.Api.Endpoints;

/// <summary>
/// The owner act that proposes ONE hypothesis from a stable liquidity-tier observation (BK-11b, ADR 0266), through the one flow of the owner acts (<see cref="OwnerActFlow"/>, P8-19): the capability <c>learning_moves_ui</c> (off by default), a fresh password for THIS act and the owner from the
/// session; the body names neither the actor nor a state. The handler writes one proposed hypothesis (source universe, knob scan_scope) and nothing else: no scan scope changes, no experiment starts, no cell state moves.
/// </summary>
internal sealed class UniverseProposeEndpoints : IEndpointModule
{
    private const string Cap = Domain.Capabilities.Capabilities.LearningMovesUi;

    public void Map(IEndpointRouteBuilder api)
    {
        api.MapPost("/cells/universe-links/{key}/propose", ProposeAsync)
            .RequireRateLimiting(SessionAuthentication.LoginRateLimitPolicy) // a password is checked here, so the sign-in limit applies
            .WithTags("Learning")
            .WithName("ProposeFromUniverseLink")
            .WithSummary("Propose ONE hypothesis (source universe, knob scan_scope from all shares to the tier, state proposed) from a stored liquidity-tier observation: the link must be an observation (the tier read in place of the parent, at least the link minimum of trades, a stable hold-out). Nothing is applied, no scan scope changes and no experiment starts; a scope hypothesis cannot be started. Needs a session, the CSRF token, the capability learning_moves_ui and the password in the body, checked on EVERY request; one HypothesisProposed account event. 403 capabilities.off, 401 auth.invalid_credentials, 423 auth.locked, 422 learning.invalid or learning.not_an_observation (with the reason), 404 learning.cell_not_found or learning.run_not_found, 409 learning.hypothesis_exists or hypotheses.open_limit, 429 auth.rate_limited")
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesRejections(conflict: true);
    }

    private static Task<Results<Ok<HypothesisMoveOutcome>, ProblemHttpResult>> ProposeAsync(string key, ProposeFromUniverseLinkRequest request, HttpContext http, ICapabilityGate gate, ICommandHandler<StepUpCommand, StepUpResult> passwordCheck, IOwnerActRunner runner, ICommandHandler<ProposeFromUniverseLinkCommand, HypothesisMoveOutcome> handler, CancellationToken cancellationToken) =>
        OwnerActFlow.RunAsync(http, gate, passwordCheck, runner, handler, Cap, request.Password, actor => Result.Success(new ProposeFromUniverseLinkCommand(key, request.BaselineRunKey ?? string.Empty, request.Reason ?? string.Empty, actor, request.Window)), AccountEventType.HypothesisProposed, o => string.Create(CultureInfo.InvariantCulture, $"hypothesis={o.Id} source=universe"), cancellationToken);
}

/// <summary>The body of the owner act that proposes a hypothesis from a stable liquidity-tier observation (BK-11b): the stored baseline run, the owner's reason, the window of the link (default all) and the password. The actor is the session's owner; nothing here names it.</summary>
/// <param name="BaselineRunKey">The stored run the hypothesis is attached to (64 lower-case hex).</param>
/// <param name="Reason">Why, 1 to 200 characters.</param>
/// <param name="Window">The window of the link: <c>all</c> (the default), <c>250d</c> or <c>60d</c>.</param>
/// <param name="Password">The owner's password, typed again for THIS act and checked on every request.</param>
internal sealed record ProposeFromUniverseLinkRequest(string? BaselineRunKey, string? Reason, string? Window, string? Password);
