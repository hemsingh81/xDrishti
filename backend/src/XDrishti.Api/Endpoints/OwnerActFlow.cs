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
/// The fixed order of a web owner act that an existing handler performs (spec P8-19, ADR 0130): the capability is asked FIRST (a switched-off act neither counts toward the sign-in lockout nor says anything about the password), then the owner types the password again for THIS act
/// (<see cref="OwnerAct"/>: step-up window neither opened, extended nor used up), then the body is turned into the command with the actor of the session, then the runner asks the gate again, calls the handler and writes the audit row. Nothing in a body can name the actor.
/// </summary>
internal static class OwnerActFlow
{
    /// <summary>Runs the act; the answer is the handler's outcome (200) or the problem to answer with. Never cached.</summary>
    public static async Task<Results<Ok<TOutcome>, ProblemHttpResult>> RunAsync<TCommand, TOutcome>(
        HttpContext http,
        ICapabilityGate gate,
        ICommandHandler<StepUpCommand, StepUpResult> passwordCheck,
        IOwnerActRunner runner,
        ICommandHandler<TCommand, TOutcome> handler,
        string capability,
        string? password,
        Func<string, Result<TCommand>> command,
        AccountEventType audit,
        Func<TOutcome, string> detail,
        CancellationToken cancellationToken)
        where TCommand : ICommand
    {
        http.Response.Headers.CacheControl = "no-store";
        if (await OwnerAct.RefusedWhileOffAsync(http, gate, capability, cancellationToken) is { } off)
        {
            return off;
        }

        var owner = await OwnerAct.AuthorizeAsync(http, passwordCheck, password, cancellationToken);
        if (owner.IsFailure)
        {
            return ProblemResults.From(owner.Error, http);
        }

        var built = command(owner.Value);
        if (built.IsFailure)
        {
            return Problem(built.Error, http);
        }

        var result = await runner.RunAsync(capability, handler, built.Value, owner.Value, audit, detail, cancellationToken);
        return result.IsFailure ? Problem(result.Error, http) : TypedResults.Ok(result.Value);
    }

    /// <summary>A refused input of the learning moves is 422, as in the Lab; every other failure is the shared mapping.</summary>
    private static ProblemHttpResult Problem(Error error, HttpContext http) =>
        error.Code == LearningErrors.InvalidCode
            ? TypedResults.Problem(ProblemResults.Create(StatusCodes.Status422UnprocessableEntity, error.Message, error.Code))
            : ProblemResults.From(error, http);
}
