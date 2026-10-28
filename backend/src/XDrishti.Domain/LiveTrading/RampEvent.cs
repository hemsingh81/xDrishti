using XDrishti.Domain.Common;

namespace XDrishti.Domain.LiveTrading;

/// <summary>A ramp event to store (spec P11-04): the move, who made it and why. The table checks the same rules again.</summary>
public sealed record RampEvent
{
    public const int MaxReason = 300;

    private RampEvent()
    {
    }

    public required Guid AccountId { get; init; }

    public required string Kind { get; init; }

    public int? FromStep { get; init; }

    public required int ToStep { get; init; }

    public required bool Hold { get; init; }

    public required string Actor { get; init; }

    public required string Reason { get; init; }

    /// <summary>
    /// The owner's step move. First step: none to S1. Lowering to any lower step is allowed. Raising needs every evidence gate to pass, and the gates are built in P11-04b: until they exist none can pass, so a raise is refused with
    /// <c>live.gates_not_met</c> (an input that cannot be read is never a pass). A step is never skipped upwards.
    /// </summary>
    public static Result<RampEvent> OwnerSetStep(Guid accountId, RampState? current, int toStep, string? actor, string? reason)
    {
        if (actor is not { } a || !a.StartsWith("owner:", StringComparison.Ordinal) || a.Length <= "owner:".Length || a.Length > 100)
        {
            return Result.Failure<RampEvent>(Error.Forbidden("live.owner_only", "Only the signed-in owner sets the ramp step."));
        }

        var clean = reason?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > MaxReason)
        {
            return Result.Failure<RampEvent>(RampErrors.Invalid("reason", $"Give a reason of 1 to {MaxReason} characters."));
        }

        if (!RampSteps.IsStep(toStep))
        {
            return Result.Failure<RampEvent>(RampErrors.Invalid("toStep", "The step is 1, 2 or 3."));
        }

        if (current is null)
        {
            return toStep == RampSteps.First
                ? Result.Success(Build(accountId, null, toStep, false, a, clean))
                : Result.Failure<RampEvent>(Error.Conflict(RampErrors.StepNotAdjacentCode, "The first step is S1."));
        }

        if (toStep == current.Step)
        {
            return Result.Failure<RampEvent>(RampErrors.Invalid("toStep", "The account is at this step already."));
        }

        if (toStep > current.Step)
        {
            return Result.Failure<RampEvent>(toStep == current.Step + 1
                ? Error.Conflict(RampErrors.GatesNotMetCode, "The evidence gates are not built yet (P11-04b); until they exist none can pass, so the step cannot be raised.")
                : Error.Conflict(RampErrors.StepNotAdjacentCode, "A step is never skipped upwards."));
        }

        return Result.Success(Build(accountId, current.Step, toStep, current.Hold, a, clean));
    }

    private static RampEvent Build(Guid accountId, int? from, int to, bool hold, string actor, string reason) => new()
    {
        AccountId = accountId,
        Kind = RampEventKinds.SetStep,
        FromStep = from,
        ToStep = to,
        Hold = hold,
        Actor = actor,
        Reason = reason,
    };
}
