using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Market.Calendar;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>What the hold-out lets a day-by-day simulation read (SR-04, open question 1 (a), ADR 0293).</summary>
/// <param name="End">The last session the simulation may run to: the end asked for, or the session before the block's cut when the look is not taken.</param>
/// <param name="ReadThrough">The session the replay's clamp is widened to when the simulation runs inside the block; null when it stays before the cut.</param>
/// <param name="Note">Why the end is what it is, in words, with the setting snapshot hash of an automatic look; null when the hold-out is not reached.</param>
internal sealed record SimHoldOutAnswer(DateOnly End, DateOnly? ReadThrough, string? Note);

/// <summary>
/// The hold-out rule of the day-by-day simulation (SR-04, open question 1 answered (a); ADR 0293). A range that stays before the block in force's cut reads nothing of the hold-out. A range that reaches block 1 needs block 1's fresh strategy look: when it is stored the simulation
/// may run inside the block (sealed: the run is isolated and recorded as a <c>system_replay</c> look); when it is not, the look is <b>taken automatically</b> through the HO-01b claim of the seal service (<see cref="StrategyAutoLookStep.ClaimAsync"/>, the existing <c>holdout</c> Lab job,
/// recorded as auto with the hold-out settings hash, never as an owner approval) under <c>Confidence:HoldOut:AutoUnlock</c>, and the end is clamped to the cut until the look is stored (Continue extends it then). An unavailable look (the option off, a filling block, no version that
/// passes every development row, a later block) never counts as taken: the end is clamped and the note says why. Reads the stored chain and the bar totals; the only write is the queued look, through the seal service.
/// </summary>
internal sealed class SimDaysHoldOut(HoldOutEpochs epochs, IHoldOutStore store, StrategyAutoLookStep claim, LabHoldOutGuard guard, HoldOutOptions options, ITradingCalendar calendars, TimeProvider time)
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

    /// <summary>How far a simulation that wants to reach <paramref name="wanted"/> may run, taking block 1's look automatically when it is needed and free.</summary>
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

        if (await store.HasFreshLookAsync(block.Start, HoldOutUnlocker.StrategyKind, cancellationToken))
        {
            return new SimHoldOutAnswer(wanted, wanted, $"Block 1's strategy look is taken; the simulation runs inside hold-out block {block.Start:yyyy-MM-dd} sealed (isolated, recorded as a system_replay look). Hold-out settings {hash}.");
        }

        if (!options.AutoUnlock)
        {
            return new SimHoldOutAnswer(cut, null, Clamped($"block 1's look is not taken and Confidence:HoldOut:AutoUnlock is off (hold-out settings {hash})."));
        }

        if (current.State == HoldOutBlockState.Filling)
        {
            return new SimHoldOutAnswer(cut, null, Clamped($"block 1 is filling until {block.HistoryLast:yyyy-MM-dd}, so its look cannot be taken yet."));
        }

        return await claim.ClaimAsync(block, Actor, cancellationToken) is { } queued
            ? new SimHoldOutAnswer(cut, null, Clamped($"block 1's strategy look was claimed automatically ({queued}; the look runs as the hold-out Lab job, recorded as auto, hold-out settings {hash}); Continue extends the end once it is stored."))
            : new SimHoldOutAnswer(cut, null, Clamped($"block 1's look is unavailable: no strategy version passes every development row (hold-out settings {hash})."));
    }
}
