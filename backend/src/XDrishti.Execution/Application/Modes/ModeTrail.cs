using XDrishti.Domain.Execution;
using XDrishti.Execution.Application.Ports;
using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Modes;

/// <summary>What the record says about an account now: the effective mode name (the database function <c>execution.effective_mode</c>: the newest trail row in force, else the configured mode, never assisted on a paper account) and the global switch.</summary>
public sealed record ModeRecord(string Mode, bool ExecutionEnabled);

/// <summary>The mode trail as the execution service sees it: read the effective mode, and append a system demotion (the only thing xd_exec may write there; the trigger refuses anything that is not lower).</summary>
public interface IModeTrail
{
    /// <summary>Null when the record cannot be read.</summary>
    Task<ModeRecord?> ReadAsync(Guid accountId, CancellationToken cancellationToken);

    /// <summary>True when a row was written; false when the database refused it (for example because the account was already lowered).</summary>
    Task<bool> DemoteAsync(Guid accountId, LadderMode from, LadderMode to, string code, CancellationToken cancellationToken);
}

/// <summary>
/// The mode source over the trail (spec P12-07, ADR 0163). Only the rungs that exist are mapped: <c>assisted</c> reads as <see cref="ExecMode.Assisted"/>, which the account gate does not accept for executing, and any other name (including
/// <c>semi_auto</c> and <c>auto_live</c>, which no row can hold) reads as <see cref="ExecMode.Paper"/>. No answer when the record cannot be read. **Composed by no host in this slice**: the closed source stays.
/// </summary>
public sealed class ModeTrailSource(IModeTrail trail) : IModeSource
{
    public async Task<ModeAnswer?> AnswerAsync(Guid accountId, CancellationToken cancellationToken)
    {
        if (await trail.ReadAsync(accountId, cancellationToken).ConfigureAwait(false) is not { } record)
        {
            return null;
        }

        var mode = ExecutionModes.TryParse(record.Mode) switch
        {
            LadderMode.ManualLive => ExecMode.ManualLive,
            LadderMode.Assisted => ExecMode.Assisted,
            _ => ExecMode.Paper,
        };
        return new ModeAnswer(mode, record.ExecutionEnabled);
    }
}

/// <summary>
/// The system's way down (spec P12-07 D4): reads the effective mode, asks the pure <see cref="DemotionPolicy"/> and writes at most one lower row, recorded as <c>system:mode-guard</c>. Idempotent (an account already at the target decides nothing). It cannot raise a mode:
/// the policy answers only a lower rung and the trigger refuses anything else. Composed by no host in this slice.
/// </summary>
public sealed class ModeGuard(IModeTrail trail)
{
    public async Task<DemotionDecision?> RunAsync(Guid accountId, DemotionFacts facts, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(facts);
        if (await trail.ReadAsync(accountId, cancellationToken).ConfigureAwait(false) is not { } record || ExecutionModes.TryParse(record.Mode) is not { } current)
        {
            return null;
        }

        if (DemotionPolicy.Evaluate(current, facts) is not { } decision || !ExecutionModes.IsDemotion(current, decision.Target))
        {
            return null;
        }

        return await trail.DemoteAsync(accountId, current, decision.Target, decision.Code, cancellationToken).ConfigureAwait(false) ? decision : null;
    }
}
