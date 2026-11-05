using System.Globalization;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Lab;

/// <summary>
/// The placebo set and the mirror gate of a robust Lab run (spec CF-07 part B, D2 and D3; ADR 0209). The real out-of-sample signals that the random-entry null restates are walked again under the 20 fixed shapes of <see cref="PlaceboShapes"/> (the mirror, later entries,
/// other sessions, other instruments) with the same risk fraction, target multiple, fills and charge sheet; the real mean is ranked among the comparable placebos by <see cref="PlaceboSet"/> and the mirror alone is read by <see cref="MirrorGate"/>. The request it is given
/// ends at the development end, so no hold-out session is read. Nothing is written here; the ledger row of the family-level look is the caller's.
/// </summary>
internal sealed class LabPlacebos(IQueryHandler<RunPlaceboWalkQuery, Result<PlaceboWalkRun>> walks, NullOptions options)
{
    /// <summary>The method written on the ledger row of the placebo set (the trial kind is <c>null</c>: one family-level look, CF-06 D3).</summary>
    public const string Method = "placebo";

    /// <summary>The (signal, shape) pairs that have a placement at all, for the progress total.</summary>
    public static int Steps(IReadOnlyList<PlaceboSignal> signals)
    {
        ArgumentNullException.ThrowIfNull(signals);
        var order = PlaceboShapes.RollOrder(signals.Select(s => s.InstrumentId));
        return signals.Sum(s => PlaceboShapes.All.Count(shape => PlaceboShapes.Place(shape, s, order) is not null));
    }

    /// <param name="request">The request of the null walk: first exit candidate, no parameters, ending at the development end.</param>
    /// <param name="signals">The real trades that can be restated.</param>
    /// <param name="realMeanR">Their mean net R.</param>
    /// <param name="oos">All out-of-sample trades of the verdict (the coverage is read against it).</param>
    /// <param name="progress">Told the running count of pairs walked.</param>
    /// <param name="notes">The notes of the block; the limits of this figure are added.</param>
    /// <param name="noSignalsReason">Said when there is no signal to walk.</param>
    /// <param name="cancellationToken">Cancels the walk; a cancel stores nothing.</param>
    public async ValueTask<(PlaceboSetView Set, MirrorView Mirror)> RunAsync(SimulationRequest request, IReadOnlyList<PlaceboSignal> signals, double realMeanR, int oos, IProgress<int>? progress, List<string> notes, string noSignalsReason, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(signals);
        ArgumentNullException.ThrowIfNull(notes);
        var c = CultureInfo.InvariantCulture;
        var minPlaced = Math.Max(RandomEntryNull.MinTrades, (int)Math.Ceiling(oos * LabNulls.MinCoverage));
        if (signals.Count == 0)
        {
            return Unavailable(noSignalsReason);
        }

        var order = PlaceboShapes.RollOrder(signals.Select(s => s.InstrumentId));
        notes.Add("Placebo shapes keep the real trade's risk fraction, target multiple, square-off, fills and charge sheet (at the placebo trade's own quantity); a shape is ranked only when it placed at least " + minPlaced.ToString(c) + " trades, and the strategy is ranked on the mean net R of its restated trades.");
        notes.Add("Instrument rolls go over the " + order.Count.ToString(c) + " instruments that have an out-of-sample trade, in id order; date shifts move by sessions of the same instrument inside the development window.");

        var plan = new PlaceboWalkPlan(signals, order);
        var ran = await walks.HandleAsync(new RunPlaceboWalkQuery(request, plan, progress), cancellationToken);
        if (ran.IsFailure)
        {
            return Unavailable("the placebo walk failed: " + ran.Error.Code);
        }

        var outcomes = ran.Value.Outcomes;
        var readings = new List<PlaceboReading>(PlaceboShapes.Count);
        for (var s = 0; s < PlaceboShapes.Count; s++)
        {
            var placed = outcomes.Where(o => o.Shape == s && o.NetR is not null).Select(o => o.NetR!.Value).ToList();
            var shape = PlaceboShapes.All[s];
            var onSelf = placed.Count == 0 && shape.Kind == PlaceboKind.RollInstrument && signals.All(x => PlaceboShapes.Place(shape, x, order) is null);
            readings.Add(new PlaceboReading(shape.Name, placed.Count, placed.Count == 0 ? null : placed.Average(), onSelf ? "the roll lands on the signal's own instrument (only " + order.Count.ToString(c) + " instruments have a trade)" : null));
        }

        foreach (var g in outcomes.Where(o => o.Reason is not null).GroupBy(o => o.Reason!, StringComparer.Ordinal).OrderByDescending(g => g.Count()).ThenBy(g => g.Key, StringComparer.Ordinal).Take(3))
        {
            notes.Add("placebo trades not made: " + g.Key + " (" + g.Count().ToString(c) + " of " + (signals.Count * PlaceboShapes.Count).ToString(c) + " pairs)");
        }

        var set = PlaceboSet.Evaluate(realMeanR, signals.Count, minPlaced, readings, options);
        var mirror = MirrorGate.Evaluate(realMeanR, readings[0].MeanR, readings[0].Placed, minPlaced);
        return (
            new PlaceboSetView(set.Shapes, set.Comparable, set.MinComparable, set.RealMeanR, set.RealRank, set.PValue, set.Threshold, "<=", Word(set.Status), set.Reason, [.. set.Rows.Select(r => new PlaceboRowView(r.Shape, r.Placed, r.MeanR, r.Rank, r.Comparable, r.Reason))]),
            new MirrorView(mirror.Trades, mirror.MirrorMeanR, mirror.RealMeanR, mirror.Threshold, "<=", Word(mirror.Status), mirror.Reason));
    }

    /// <summary>The set and the mirror gate when nothing could be walked: both unavailable with the reason, never a pass.</summary>
    public (PlaceboSetView Set, MirrorView Mirror) Unavailable(string reason) => (
        new PlaceboSetView(PlaceboShapes.Count, 0, options.MinPlacebos, null, null, null, NullOptions.PlaceboAlpha, "<=", "unavailable", "unavailable: " + reason, []),
        new MirrorView(0, null, null, MirrorGate.Threshold, "<=", "unavailable", "unavailable: " + reason));

    private static string Word(NullStatus status) => status switch
    {
        NullStatus.Pass => "pass",
        NullStatus.Fail => "fail",
        _ => "unavailable",
    };
}
