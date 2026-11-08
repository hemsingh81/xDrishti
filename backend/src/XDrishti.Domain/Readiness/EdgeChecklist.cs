using System.Globalization;

namespace XDrishti.Domain.Readiness;

/// <summary>
/// The 15-row go / no-go checklist of the edge (spec CF-11 D1, part A), judged from stored results. Pure: no clock, no I/O. Three states per row, Kleene style: a fail anywhere in a row's parts makes it a fail, an unavailable part with no fail makes it unavailable, and
/// <b>unavailable is never a pass</b>. A boundary equality passes ("at least"); a tiny tolerance absorbs binary rounding so that <c>0.15 x 1.25</c> equals <c>0.1875</c>.
/// </summary>
public static class EdgeChecklist
{
    /// <summary>The number of rows.</summary>
    public const int RowCount = 15;

    /// <summary>The quarter floor of row 10, in R (a producer cuts the share at it).</summary>
    public const double QuarterFloorR = -0.05;

    /// <summary>The standard errors the forward mean may sit from the out-of-sample mean (row 15).</summary>
    public const double AgreementStandardErrors = 1.5;

    /// <summary>Absorbs binary rounding in a boundary comparison (0.15 x 1.25 against 0.1875).</summary>
    private const double Tolerance = 1e-9;

    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    /// <summary>Judges the 15 rows, in order.</summary>
    public static IReadOnlyList<ChecklistRow> Evaluate(ChecklistInputs inputs, ReadinessOptions options)
    {
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(options);
        return
        [
            Registered(inputs),
            Independent(inputs, options),
            Expectancy(inputs, options),
            CostStress(inputs),
            Fill(inputs),
            TStatistic(inputs, options),
            Judged(7, "Random-entry null", "CF-07", "the real mean is above the 95th percentile of 1000 seeds", inputs.RandomEntry, inputs),
            PlaceboAndMirror(inputs),
            Judged(9, "Plateau", "CF-07", "the two neighbours keep 70 % of the base and the grid median is above 0", inputs.Plateau, inputs),
            Stability(inputs, options),
            Drawdown(inputs, options),
            Judged(12, "Hold-out", "CF-06", "used once, at least 50 % of the development out-of-sample, above 0, at least 30 trades", inputs.HoldOut, inputs),
            Flag(13, "Benchmark", "P8-07 buy-and-hold rules (the Lab verdict's benchmark comparison, ADR 0227)", "daily Sharpe of the net out-of-sample returns strictly above the same-universe equal-weight buy-and-hold's over the same days", inputs.BenchmarkBeaten, inputs, "beats", "does not beat"),
            Flag(14, "Capacity and tier", "CF-05 (the Lab verdict's tier and capacity counts, ADR 0223)", "all trades tier A or B, every order at most 1 % of the fill minute's traded value (the stored minute bar of the fill minute; unread is unavailable) and at most 0.5 % of the 20-day median", inputs.CapacityOk, inputs, "within capacity", "outside capacity"),
            PaperConfirm(inputs),
        ];
    }

    /// <summary>The Kleene AND of statuses: a fail wins, then unavailable, else pass.</summary>
    internal static RowStatus And(params RowStatus[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);
        return parts.Contains(RowStatus.Fail) ? RowStatus.Fail : parts.Contains(RowStatus.Unavailable) ? RowStatus.Unavailable : RowStatus.Pass;
    }

    private static RowStatus AtLeast(double? value, double bar) => value is not { } v || !double.IsFinite(v) ? RowStatus.Unavailable : v >= bar - Tolerance ? RowStatus.Pass : RowStatus.Fail;

    private static RowStatus AtMost(double? value, double bar) => value is not { } v || !double.IsFinite(v) ? RowStatus.Unavailable : v <= bar + Tolerance ? RowStatus.Pass : RowStatus.Fail;

    private static RowStatus Of(bool? value) => value switch { true => RowStatus.Pass, false => RowStatus.Fail, _ => RowStatus.Unavailable };

    private static string N(double? value, string format = "0.####") => value is { } v && double.IsFinite(v) ? v.ToString(format, Inv) : "unavailable";

    private static string Note(ChecklistInputs inputs, int row) => inputs.Notes.TryGetValue(row, out var note) && note.Length > 0 ? " " + note : string.Empty;

    private static ChecklistRow Make(int number, string name, string producer, RowStatus status, string threshold, string value, ChecklistInputs inputs) =>
        new(number, name, producer, status, threshold, value + (status == RowStatus.Pass ? string.Empty : Note(inputs, number)));

    private static ChecklistRow Judged(int number, string name, string producer, string threshold, RowStatus status, ChecklistInputs inputs) =>
        Make(number, name, producer, status, threshold, status switch { RowStatus.Pass => "passed", RowStatus.Fail => "failed", _ => "unavailable" } + " (the producer's own judgement)", inputs);

    private static ChecklistRow Flag(int number, string name, string producer, string threshold, bool? value, ChecklistInputs inputs, string yes, string no) =>
        Make(number, name, producer, Of(value), threshold, value switch { true => yes, false => no, _ => "unavailable" }, inputs);

    private static ChecklistRow Registered(ChecklistInputs i) =>
        Flag(1, "Rationale and falsifier registered, plan frozen", "CF-06", "present, hash frozen", i.Registered, i, "registered and frozen", "not registered");

    private static ChecklistRow Independent(ChecklistInputs i, ReadinessOptions o)
    {
        var status = And(AtLeast(i.NEff, o.MinNEff), AtLeast(i.MinTradesPerCell, o.MinTradesPerCell));
        return Make(
            2, "Independent trades (out of sample, pre-hold-out)", "CF-01", status,
            string.Create(Inv, $"n_eff at least {o.MinNEff:0.##} and at least {o.MinTradesPerCell} per used cell"),
            string.Create(Inv, $"n_eff {N(i.NEff, "0.#")}, fewest per cell {(i.MinTradesPerCell is { } c ? c.ToString(Inv) : "unavailable")}"), i);
    }

    private static ChecklistRow Expectancy(ChecklistInputs i, ReadinessOptions o)
    {
        double? need = i.Factor is { } f && double.IsFinite(f) ? o.ExpectancyR * f : null;
        var status = need is { } n ? AtLeast(i.ExpectancyR, n) : RowStatus.Unavailable;
        return Make(
            3, "Net expectancy at 1x costs (pre-hold-out)", "Lab result, CF-06 ledger", status,
            string.Create(Inv, $"at least {o.ExpectancyR:0.##} x Factor(N) R = {N(need)} R"),
            string.Create(Inv, $"{N(i.ExpectancyR)} R, Factor {N(i.Factor)}"), i);
    }

    private static ChecklistRow CostStress(ChecklistInputs i) =>
        Make(4, "Cost stress", "CF-05", And(i.CostStress15, i.CostStress2), "1.5x at least +0.08 R and 2x above 0", string.Create(Inv, $"1.5x {Word(i.CostStress15)}, 2x {Word(i.CostStress2)} (the producer's own judgement)"), i);

    private static ChecklistRow Fill(ChecklistInputs i) =>
        Make(5, "Fill one minute later", "CF-05", i.FillDelay, "at least 70 % of the expectancy kept, per signal", Word(i.FillDelay) + " (the producer's own judgement)", i);

    private static ChecklistRow TStatistic(ChecklistInputs i, ReadinessOptions o)
    {
        double? t = i.ExpectancyR is { } m && i.SdR is { } sd && i.NEff is { } n && sd > 0 && n > 0 && double.IsFinite(m) && double.IsFinite(sd) && double.IsFinite(n) ? m / (sd / Math.Sqrt(n)) : null;
        double? need = i.Factor is { } f && double.IsFinite(f) ? o.TStatistic * f : null;
        var status = need is { } bar ? AtLeast(t, bar) : RowStatus.Unavailable;
        return Make(
            6, "t-statistic of the out-of-sample trades (pre-hold-out)", "Lab result, CF-01", status,
            string.Create(Inv, $"at least {o.TStatistic:0.##} x Factor(N) = {N(need, "0.###")}, SE = sd / sqrt(n_eff)"),
            string.Create(Inv, $"t {N(t, "0.###")}"), i);
    }

    private static ChecklistRow PlaceboAndMirror(ChecklistInputs i) =>
        Make(8, "Placebo and mirror", "CF-07", And(i.Placebo, i.Mirror), "the mirror is at most 0 and the real strategy ranks first of 21", string.Create(Inv, $"placebo {Word(i.Placebo)}, mirror {Word(i.Mirror)} (the producer's own judgement)"), i);

    private static ChecklistRow Stability(ChecklistInputs i, ReadinessOptions o)
    {
        var status = And(AtLeast(i.QuarterShare, o.MinQuarterShare), AtMost(i.MaxYearShare, o.MaxYearShare), AtMost(i.MaxInstrumentShare, o.MaxInstrumentShare));
        return Make(
            10, "Stability", "Lab result (the quarter and instrument shares have no source yet)", status,
            string.Create(Inv, $"at least {o.MinQuarterShare:P0} of calendar quarters at or above {QuarterFloorR} R; no year over {o.MaxYearShare:P0} of the total R; no instrument over {o.MaxInstrumentShare:P0}"),
            string.Create(Inv, $"quarters {N(i.QuarterShare, "P0")}, largest year {N(i.MaxYearShare, "P0")}, largest instrument {N(i.MaxInstrumentShare, "P0")}"), i);
    }

    private static ChecklistRow Drawdown(ChecklistInputs i, ReadinessOptions o)
    {
        var within = i.ObservedMaxDrawdownR is { } obs && i.McDrawdownP90 is { } p90 ? AtMost(obs, p90) : RowStatus.Unavailable;
        var status = And(within, AtMost(i.McDrawdownP95, o.DrawdownBudgetR));
        return Make(
            11, "Drawdown", "P5-05 Monte Carlo block", status,
            string.Create(Inv, $"observed within the Monte Carlo 90th percentile and the 95th under {o.DrawdownBudgetR:0.##} R"),
            string.Create(Inv, $"observed {N(i.ObservedMaxDrawdownR, "0.##")} R, 90th {N(i.McDrawdownP90, "0.##")} R, 95th {N(i.McDrawdownP95, "0.##")} R"), i);
    }

    private static ChecklistRow PaperConfirm(ChecklistInputs i)
    {
        var agreement = i.ExpectancyR is { } oos && i.ForwardMeanR is { } fwd && i.ForwardStandardError is { } se && se > 0 && double.IsFinite(oos) && double.IsFinite(fwd) && double.IsFinite(se)
            ? Math.Abs(fwd - oos) <= (AgreementStandardErrors * se) + Tolerance ? RowStatus.Pass : RowStatus.Fail
            : RowStatus.Unavailable;
        var status = And(Of(i.TwentyTradeCheckPassed), Of(i.PhaseCheckMet), agreement);
        return Make(
            15, "Paper confirm", "P3-09, P10-06, CF-08", status,
            "the P3-09 20-trade check passed, the P10 phase check MET, and the edge-series mean within 1.5 SE of the out-of-sample mean",
            string.Create(Inv, $"20-trade check {Word(Of(i.TwentyTradeCheckPassed))}, phase check {Word(Of(i.PhaseCheckMet))}, forward mean {N(i.ForwardMeanR)} R vs {N(i.ExpectancyR)} R (SE {N(i.ForwardStandardError)})"), i);
    }

    private static string Word(RowStatus status) => status switch { RowStatus.Pass => "pass", RowStatus.Fail => "fail", _ => "unavailable" };
}
