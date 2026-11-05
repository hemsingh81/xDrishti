using System.Globalization;
using XDrishti.Domain.Confidence;
using XDrishti.Domain.Lab;

namespace XDrishti.Application.Lab;

/// <summary>
/// The plateau gate on the swept parameters of a robust Lab run (spec CF-07 part C, D4; ADR 0210). It reads the stored points of the in-sample sensitivity sweep (<see cref="SensitivityView"/>): no simulation, no new look in the ledger (every sweep point is
/// already a ledger row of its own). Each parameter is judged by <see cref="PlateauGate"/>; the gate passes when all do. The sweep ran over the run's whole window, so when that window reaches the hold-out block (an explicit window may) the gate is
/// unavailable and says so: it never reads a figure that depends on a hold-out session. With <c>Enforce</c> off (the default) the outcome, the gates and the limits are exactly what they were; the figure is added beside them.
/// </summary>
internal sealed class LabPlateau(PlateauOptions options)
{
    /// <summary>True when the gate is computed.</summary>
    public bool IsOn => options.IsOn;

    private static string Basis(DateOnly developmentEnd, DateOnly sweepEnd, string exit) =>
        "in-sample sweep, one parameter at a time, plain runs of the default exit (" + exit + ") over the run's window, read as stored; development window ending "
        + developmentEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
        + (developmentEnd < sweepEnd ? " (the sweep reaches " + sweepEnd.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", inside the hold-out block)" : string.Empty)
        + "; the declared value is never changed by this gate, a plateau centre is a suggestion for a new strategy version";

    /// <param name="declared">The declared parameters of the strategy in order (name, value).</param>
    /// <param name="schedule">The sweep that was run: the parameters it took.</param>
    /// <param name="sensitivity">The stored sweep.</param>
    /// <param name="developmentEnd">The last session that is not in the hold-out block.</param>
    /// <param name="sweepEnd">The last session the sweep ran over.</param>
    public PlateauView Read(IReadOnlyList<KeyValuePair<string, double>> declared, IReadOnlyList<string> schedule, SensitivityView sensitivity, DateOnly developmentEnd, DateOnly sweepEnd)
    {
        ArgumentNullException.ThrowIfNull(declared);
        ArgumentNullException.ThrowIfNull(schedule);
        ArgumentNullException.ThrowIfNull(sensitivity);
        var basis = Basis(developmentEnd, sweepEnd, sensitivity.Exit);
        PlateauView Make(string status, string reason, IReadOnlyList<PlateauParameterView> parameters) =>
            new(options.Mode.ToString(), options.Enforce, options.KeyText(), options.SnapshotHash(), options.Retention, options.MinPoints, ">=", status, reason, false, basis, parameters);

        if (developmentEnd < sweepEnd)
        {
            return Make("unavailable", "unavailable: the sweep ran over a window that reaches the hold-out block, and a figure that depends on a hold-out session is never read", []);
        }

        var results = new List<PlateauResult>();
        foreach (var name in schedule)
        {
            var found = declared.Where(d => string.Equals(d.Key, name, StringComparison.Ordinal)).Select(d => (double?)d.Value).FirstOrDefault();
            if (found is not { } value)
            {
                continue;
            }

            var baseline = sensitivity.Base is { } b ? new PlateauPoint(value, b.Trades, b.MeanR, b.Error) : new PlateauPoint(value, 0, null, "the base run was not stored");
            var swept = sensitivity.Parameters.Where(p => string.Equals(p.Param, name, StringComparison.Ordinal)).SelectMany(p => p.Points).Select(p => new PlateauPoint(p.Value, p.Trades, p.MeanR, p.Error)).ToList();
            results.Add(PlateauGate.Evaluate(name, value, baseline, swept, options));
        }

        var (status, reason) = PlateauGate.Overall(results);
        return Make(Word(status), reason, [.. results.Select(ViewOf)]);
    }

    /// <summary>The verdict with the capped outcome when <c>Enforce</c> is on and the gate is not a pass (a fail, or unavailable): a PASS becomes MARGINAL, nothing else changes, nothing is raised and the six gates are untouched.</summary>
    public LabVerdictView Apply(LabVerdictView verdict)
    {
        ArgumentNullException.ThrowIfNull(verdict);
        if (verdict.Nulls?.Plateau is not { } view || !options.Enforce || string.Equals(view.Status, "pass", StringComparison.Ordinal) || !string.Equals(verdict.Outcome, "PASS", StringComparison.Ordinal))
        {
            return verdict;
        }

        return verdict with { Outcome = "MARGINAL", Limits = [.. verdict.Limits, "capped at MARGINAL by the plateau gate (" + view.Status + ") (enforced)"], Nulls = verdict.Nulls with { Plateau = view with { Capped = true } } };
    }

    private static PlateauParameterView ViewOf(PlateauResult r) => new(
        r.Parameter, r.Declared, r.ValidPoints, r.BaseMeanR, r.LowerValue, r.LowerRetention, r.UpperValue, r.UpperRetention, r.Median, r.CentreValue, r.CentreMeanR, r.Reading, Word(r.Status), r.Reason,
        [.. r.Points.Select(p => new PlateauPointView(p.Value, p.Value == r.Declared, p.Trades, p.MeanR, r.BaseMeanR is > 0 && p.IsValid ? p.MeanR / r.BaseMeanR : null, p.Error))]);

    private static string Word(NullStatus status) => status switch
    {
        NullStatus.Pass => "pass",
        NullStatus.Fail => "fail",
        _ => "unavailable",
    };
}
