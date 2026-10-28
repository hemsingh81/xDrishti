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

    /// <summary>The frozen gate text and its hash, on a step raised on evidence (<c>set_step</c> up, <c>auto_step_up</c>).</summary>
    public GateSnapshot? Gates { get; init; }

    /// <summary>The live switches row an automatic step-up rests on.</summary>
    public long? SettingsId { get; init; }

    public string? SettingsHash { get; init; }

    /// <summary>
    /// The owner's step move. First step: none to S1. Lowering to any lower step is allowed. Raising needs every evidence gate to pass, and the gates are built in P11-04b: until they exist none can pass, so a raise is refused with
    /// <c>live.gates_not_met</c> (an input that cannot be read is never a pass). A step is never skipped upwards.
    /// </summary>
    public static Result<RampEvent> OwnerSetStep(Guid accountId, RampState? current, int toStep, string? actor, string? reason, GateSnapshot? gates = null)
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
            if (toStep != current.Step + 1)
            {
                return Result.Failure<RampEvent>(Error.Conflict(RampErrors.StepNotAdjacentCode, "A step is never skipped upwards."));
            }

            if (current.Hold)
            {
                return Result.Failure<RampEvent>(Error.Conflict(RampErrors.HoldActiveCode, "A hold blocks raising the step: clear it first."));
            }

            if (toStep == RampSteps.Third && !current.ExitConfirmed)
            {
                return Result.Failure<RampEvent>(Error.Conflict(RampErrors.ExitNotConfirmedCode, "S3 needs the owner's recorded exit confirmation first."));
            }

            if (gates is not { Eligible: true })
            {
                var failing = gates is null ? "the gates could not be read" : string.Join(", ", gates.Gates.Where(g => g.Code != RampGate.CostDrag && !g.Passes).Select(g => $"{g.Code} {g.State}"));
                return Result.Failure<RampEvent>(Error.Conflict(RampErrors.GatesNotMetCode, $"The evidence gates are not all met: {failing}. An unreadable gate is never a pass."));
            }

            return Result.Success(Build(accountId, current.Step, toStep, false, a, clean) with { Gates = gates });
        }

        return Result.Success(Build(accountId, current.Step, toStep, current.Hold, a, clean));
    }

    /// <summary>The owner releases a hold. Refused while the breach conditions still hold (the caller passes what still holds); needs a reason.</summary>
    public static Result<RampEvent> OwnerClearHold(Guid accountId, RampState? current, string? actor, string? reason, IReadOnlyList<Breach> stillHolding) =>
        OwnerAct(accountId, current, actor, reason, RampEventKinds.ClearHold, false, current is { Hold: true } ? null : Error.Conflict(RampErrors.NoHoldCode, "There is no hold to clear."), stillHolding.Count > 0
            ? Error.Conflict(RampErrors.HoldConditionActiveCode, "The hold cannot be cleared yet: " + string.Join("; ", stillHolding.Select(b => $"{b.Code} {b.Reason}")) + ".")
            : null);

    /// <summary>The owner records that the small-live exit report was read and is met (needed before S3). Not a step move.</summary>
    public static Result<RampEvent> OwnerExitConfirmed(Guid accountId, RampState? current, string? actor, string? reason) =>
        OwnerAct(accountId, current, actor, reason, RampEventKinds.ExitConfirmed, current?.Hold ?? false, null, null);

    private static Result<RampEvent> OwnerAct(Guid accountId, RampState? current, string? actor, string? reason, string kind, bool hold, Error? first, Error? second)
    {
        if (actor is not { } a || !a.StartsWith("owner:", StringComparison.Ordinal) || a.Length <= "owner:".Length || a.Length > 100)
        {
            return Result.Failure<RampEvent>(Error.Forbidden("live.owner_only", "Only the signed-in owner does this."));
        }

        var clean = reason?.Trim();
        if (string.IsNullOrEmpty(clean) || clean.Length > MaxReason)
        {
            return Result.Failure<RampEvent>(RampErrors.Invalid("reason", $"Give a reason of 1 to {MaxReason} characters."));
        }

        if (current is null)
        {
            return Result.Failure<RampEvent>(Error.Conflict(RampErrors.NoStepCode, "The account has no ramp step."));
        }

        if (first is not null || second is not null)
        {
            return Result.Failure<RampEvent>((first ?? second)!);
        }

        return Result.Success(Build(accountId, current.Step, current.Step, hold, a, clean) with { Kind = kind });
    }

    /// <summary>A hold written by the rule: raising is blocked, the step stays.</summary>
    public static RampEvent RuleHold(Guid accountId, RampState current, string reason) =>
        Build(accountId, current.Step, current.Step, true, RampEventKinds.RuleActor, Trim(reason)) with { Kind = RampEventKinds.Hold };

    /// <summary>A step-down written by the rule: one step lower, no hold (a new owner step, gates applying, lifts it).</summary>
    public static RampEvent RuleStepDown(Guid accountId, RampState current, string reason) =>
        Build(accountId, current.Step, current.Step - 1, false, RampEventKinds.RuleActor, Trim(reason)) with { Kind = RampEventKinds.StepDown };

    /// <summary>
    /// An automatic step-up written by the rule: only from S1 to S2 (never above step 2), with no hold, every gate passing and the stored switch snapshot it rests on. Null when any of that is not so.
    /// </summary>
    public static RampEvent? RuleAutoStepUp(Guid accountId, RampState current, GateSnapshot? gates, LiveSettings settings)
    {
        ArgumentNullException.ThrowIfNull(current);
        ArgumentNullException.ThrowIfNull(settings);
        return current is { Hold: false, Step: < 2 } && gates is { Eligible: true } && settings is { AutoRampUp: true, Id: not null, Hash: not null }
            ? Build(accountId, current.Step, current.Step + 1, false, RampEventKinds.RuleActor, "Automatic step-up: every evidence gate passes and live.auto_ramp_up is on.") with { Kind = RampEventKinds.AutoStepUp, Gates = gates, SettingsId = settings.Id, SettingsHash = settings.Hash }
            : null;
    }

    private static string Trim(string reason) => reason.Length <= MaxReason ? reason : reason[..MaxReason];

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
