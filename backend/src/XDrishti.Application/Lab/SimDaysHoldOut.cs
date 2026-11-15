using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>What the hold-out lets a day-by-day simulation read (SR-04, open question 1 (a), ADR 0293). A pure decision: nothing is claimed until <see cref="SimDaysHoldOut.ClaimAsync"/>.</summary>
/// <param name="End">The last session the simulation may run to: the end asked for, or the session before the block's cut when the look is not stored.</param>
/// <param name="ReadThrough">The session the replay's clamp is widened to when the simulation runs inside the block; null when it stays before the cut.</param>
/// <param name="Note">Why the end is what it is, in words; null when the hold-out is not reached.</param>
/// <param name="Claim">Block 1 whose free strategy look the caller should claim once every other check of its request has passed; null when there is nothing to claim.</param>
internal sealed record SimHoldOutAnswer(DateOnly End, DateOnly? ReadThrough, string? Note, HoldOutBlock? Claim = null);

/// <summary>
/// The hold-out rule of the day-by-day simulation (SR-04, open question 1 answered (a); ADR 0293). A range that stays before the block in force's cut reads nothing of the hold-out. A range that reaches block 1 needs block 1's fresh strategy look: when it is stored the simulation
/// may run inside the block (sealed: the run is isolated and recorded as a <c>system_replay</c> look); when it is not, the look is <b>taken automatically</b> through the HO-01b claim of the seal service (<see cref="StrategyAutoLookStep.ClaimAsync"/>, the existing <c>holdout</c> Lab job,
/// recorded as auto with the hold-out settings hash, never as an owner approval) under <c>Confidence:HoldOut:AutoUnlock</c>, and the end is clamped to the cut until the look is stored (Continue extends it then). An unavailable look (the option off, a filling block, no version that
/// passes every development row, a later block) never counts as taken: the end is clamped and the note says why. Reads the stored chain and the bar totals; the only write is the queued look, through the seal service.
/// </summary>
internal sealed class SimDaysHoldOut(HoldOutEpochs epochs, IHoldOutStore store, StrategyAutoLookStep claim, ILabRunStore runs, LabHoldOutGuard guard, HoldOutOptions options, ITradingCalendar calendars, TimeProvider time)
{
    /// <summary>The actor of a look the simulation takes (its mode reads <c>auto</c>).</summary>
    public const string Actor = "system:sim-days";

    /// <summary>The first stored session and the last complete one (never the session in progress); null when no bar is stored.</summary>
    public async ValueTask<(DateOnly First, DateOnly LastComplete)?> HistoryAsync(CancellationToken cancellationToken)
    {
        if (await guard.HistoryAsync(cancellationToken) is not { } history)
        {
            return null;
        }

        var closed = (await calendars.GetAsync(cancellationToken)).LastClosedSession(time.GetUtcNow());
        return (history.First, history.Last < closed ? history.Last : closed);
    }

    /// <summary>How far a simulation that wants to reach <paramref name="wanted"/> may run. Reads only: a free look of block 1 comes back in <see cref="SimHoldOutAnswer.Claim"/> for the caller to claim after its own checks.</summary>
    public async ValueTask<SimHoldOutAnswer> ResolveAsync(DateOnly wanted, CancellationToken cancellationToken)
    {
        if (await epochs.InForceAsync(cancellationToken) is not { } current)
        {
            return new SimHoldOutAnswer(wanted, null, null); // no block fixed: the replay's own check refuses the start (fails closed)
        }

        var block = current.Block;
        var cut = HoldOutOptions.TrainingCut(block.Start, options.TrainingEmbargoDays);
        if (wanted <= cut)
        {
            return new SimHoldOutAnswer(wanted, null, null);
        }

        var hash = options.SnapshotHash();
        string Clamped(string why) => $"The end is clamped to {cut:yyyy-MM-dd}, the day before hold-out block {block.Start:yyyy-MM-dd} and its embargo: {why}";
        if (block.PrevStart is not null)
        {
            return new SimHoldOutAnswer(cut, null, Clamped("the simulation takes only block 1's look; a later block's look is the evening seal's."));
        }

        if ((await store.ListFreshLooksAsync(block.Start, cancellationToken)).FirstOrDefault(u => u.Kind == HoldOutUnlocker.StrategyKind) is { } look)
        {
            return new SimHoldOutAnswer(wanted, wanted, $"Block 1's strategy look is stored ({look.StrategyId} v{look.Version}, mode {look.Mode}, actor {look.Actor}); the simulation runs inside hold-out block {block.Start:yyyy-MM-dd} sealed (isolated, recorded as a system_replay look). Hold-out settings {look.SettingHash}.");
        }

        if (!options.AutoUnlock)
        {
            return new SimHoldOutAnswer(cut, null, Clamped($"block 1's look is not taken and Confidence:HoldOut:AutoUnlock is off (hold-out settings {hash})."));
        }

        if (current.State is not (HoldOutBlockState.Sealed or HoldOutBlockState.Looked))
        {
            return new SimHoldOutAnswer(cut, null, Clamped($"block 1 is {HoldOutChain.Word(current.State)} until {block.HistoryLast:yyyy-MM-dd}, so its look cannot be taken yet."));
        }

        foreach (var state in new[] { LabRunState.Queued, LabRunState.Running })
        {
            if ((await runs.ListAsync(null, state, 200, cancellationToken)).Any(r => string.Equals(r.Method, HoldOutJob.Method, StringComparison.Ordinal)))
            {
                return new SimHoldOutAnswer(cut, null, Clamped($"a hold-out look is queued or running; Continue extends the end once it is stored (hold-out settings {hash})."));
            }
        }

        return new SimHoldOutAnswer(cut, null, Clamped($"block 1's strategy look is free (hold-out settings {hash})."), block);
    }

    /// <summary>
    /// Takes block 1's look automatically for an answer that names one (recorded as auto under <see cref="Actor"/>, the hold-out Lab job, never an owner approval) and returns the answer with its note saying what was done. Call only after every other check of the request passed: the look
    /// is spent for good.
    /// </summary>
    public async ValueTask<SimHoldOutAnswer> ClaimAsync(SimHoldOutAnswer answer, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(answer);
        if (answer.Claim is not { } block)
        {
            return answer;
        }

        var hash = options.SnapshotHash();
        var cut = HoldOutOptions.TrainingCut(block.Start, options.TrainingEmbargoDays);
        string Clamped(string why) => $"The end is clamped to {cut:yyyy-MM-dd}, the day before hold-out block {block.Start:yyyy-MM-dd} and its embargo: {why}";
        return await claim.ClaimAsync(block, Actor, cancellationToken) is { } queued
            ? answer with { Claim = null, Note = Clamped($"block 1's strategy look was claimed automatically ({queued}; the look runs as the hold-out Lab job, recorded as auto, hold-out settings {hash}); Continue extends the end once it is stored.") }
            : answer with { Claim = null, Note = Clamped($"block 1's look is unavailable: no strategy version passes every development row (hold-out settings {hash}).") };
    }
}
