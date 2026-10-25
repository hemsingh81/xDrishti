using System.Globalization;

namespace XDrishti.Domain.Reporting;

/// <summary>
/// Plan versus actual (P8-04): where the edge goes between the plan's R and the realised R, over the tickets graded on fills. Pure: the same lines in any order give the same answer. The chain closes by construction: the last step is the
/// remainder (exit slippage and anything else), so plan - entry slippage - charges - remainder = actual for every ticket and for the means.
/// </summary>
public static class PlanVsActual
{
    public const string StatusGraded = "graded";
    public const string StatusUncertain = "uncertain";
    public const string StatusNoPlanLevels = "no_plan_levels";
    public const string StatusNotFinished = "not_finished";
    public const string StatusNone = "none";

    private const string NoGradedReason = "No ticket has been graded on fills yet; fills arrive with paper and live trading, P10/P11.";

    private static readonly (string Step, string Label)[] Steps =
    [
        ("plan_prices", "Plan prices"),
        ("entry_slippage", "Entry slippage"),
        ("charges", "Charges"),
        ("not_attributed", "Not attributed (exit slippage and anything else)"),
        ("actual", "Actual, after charges"),
    ];

    /// <summary>Builds the answer from the approved ticket lines.</summary>
    public static PlanVsActualResult Build(IReadOnlyList<PlanVsActualLine> lines, PlanVsActualOptions options)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(options);
        var graded = new List<Ticket>();
        int uncertain = 0, noLevels = 0, notFinished = 0, without = 0;
        var missed = new List<double>();
        foreach (var line in lines)
        {
            switch (line.FillStatus)
            {
                case StatusNone:
                    without++;
                    if (line.PlanGradeTriggered && line.PlanGradeR is { } m && double.IsFinite(m))
                    {
                        missed.Add(m);
                    }

                    break;
                case StatusUncertain:
                    uncertain++;
                    break;
                case StatusNotFinished:
                    notFinished++;
                    break;
                case StatusGraded when Usable(line) is { } ticket:
                    graded.Add(ticket);
                    break;
                default:
                    noLevels++;
                    break;
            }
        }

        var counts = new PlanVsActualCounts(lines.Count, lines.Count - without, graded.Count, uncertain, noLevels, notFinished, without);
        return new PlanVsActualResult(
            counts,
            Chain(graded, options.MinTickets),
            new PlanVsActualMissed(missed.Count, missed.Count == 0 ? Metric.Empty("no approved ticket without a fill triggered") : Metric.Of(Sum(missed))),
            [
                new PlanVsActualNotDecomposed("exit_slippage", "The plan grade stores no simulated exit price, so exit slippage cannot be separated; it sits in the remainder."),
                new PlanVsActualNotDecomposed("overrides", "The effect of the owner's overrides is not a per-fill step: a ticket the owner rejected or let lapse has no fill; read it in Decisions."),
            ],
            Strategies(graded, options.MinTickets),
            options.MinTickets);
    }

    private static List<PlanVsActualStep> Chain(List<Ticket> tickets, int min)
    {
        if (tickets.Count == 0)
        {
            return [.. Steps.Select(s => new PlanVsActualStep(s.Step, s.Label, Metric.Empty(NoGradedReason), 0))];
        }

        Func<Ticket, double>[] pick = [x => x.PlanR, x => -x.SlippageR, x => -x.ChargesR, x => x.FillR - (x.PlanR - x.SlippageR - x.ChargesR), x => x.FillR];
        return [.. Steps.Select((s, i) => new PlanVsActualStep(s.Step, s.Label, Mean(tickets, pick[i], min), tickets.Count))];
    }

    private static List<PlanVsActualStrategy> Strategies(List<Ticket> tickets, int min) =>
    [
        .. tickets.GroupBy(x => x.StrategyId, StringComparer.Ordinal).OrderBy(g => g.Key, StringComparer.Ordinal).Select(g =>
        {
            var list = g.ToList();
            return new PlanVsActualStrategy(g.Key, list.Count, Mean(list, x => x.PlanR, min), Mean(list, x => x.FillR, min), Mean(list, x => x.SlippageR, min));
        }),
    ];

    private static Metric Mean(List<Ticket> tickets, Func<Ticket, double> value, int min) =>
        tickets.Count < min
            ? Metric.Insufficient(string.Create(CultureInfo.InvariantCulture, $"n {tickets.Count} of at least {min}"))
            : Metric.Of(Sum(tickets.Select(value)) / tickets.Count);

    /// <summary>Order-independent sum (ascending first), so a permutation of the lines changes no byte.</summary>
    private static double Sum(IEnumerable<double> values) => values.Order().Sum();

    private static Ticket? Usable(PlanVsActualLine l)
    {
        if (l is not { PlanR: { } plan, SlippageR: { } slip, ChargesRupees: { } charges, Quantity: { } qty, PlanEntry: { } entry, PlanStop: { } stop, FillR: { } fill })
        {
            return null;
        }

        var risk = qty * Math.Abs(entry - stop);
        var finite = double.IsFinite(risk) && double.IsFinite(plan) && double.IsFinite(slip) && double.IsFinite(charges) && double.IsFinite(fill);
        return qty > 0 && risk > 0 && finite ? new Ticket(l.StrategyId, plan, slip, charges / risk, fill) : null;
    }

    private sealed record Ticket(string StrategyId, double PlanR, double SlippageR, double ChargesR, double FillR);
}
