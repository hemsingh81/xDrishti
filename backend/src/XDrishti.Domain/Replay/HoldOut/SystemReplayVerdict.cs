using System.Globalization;
using XDrishti.Domain.Lab;

namespace XDrishti.Domain.Replay.HoldOut;

/// <summary>The verdict of the system replay over a hold-out block: the status word (<see cref="HoldOutStatus"/>) and the reason in words.</summary>
public sealed record SystemReplayVerdict(string Status, string Reason)
{
    /// <summary>The SR-02 answer that fails the rule.</summary>
    public const string BaselineBetter = "baseline_better";

    /// <summary>The SR-02 answers that are a statement about the arms.</summary>
    private static readonly string[] Answers = ["selection_better", "no_measurable_difference", BaselineBetter];

    /// <summary>
    /// Pass needs at least <c>MinTrades</c> closed trades, CAGR above 0, maximum drawdown at or under the limit and an arm comparison that is not <c>baseline_better</c>, all net. Fewer trades, a figure that is not net or any unavailable figure is <c>inconclusive</c>
    /// (never a pass); with enough trades, any other failed condition is <c>fail</c>. Pure.
    /// </summary>
    public static SystemReplayVerdict Evaluate(SystemReplayPassRule rule, SystemReplayFigures figures)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(figures);
        if (figures.Trades is not { } trades)
        {
            return new(HoldOutStatus.Inconclusive, "unavailable: the number of closed trades");
        }

        if (trades < rule.MinTrades)
        {
            return new(HoldOutStatus.Inconclusive, string.Create(CultureInfo.InvariantCulture, $"{trades} closed trades is fewer than the {rule.MinTrades} the rule needs"));
        }

        if (!figures.NetBasis)
        {
            return new(HoldOutStatus.Inconclusive, "unavailable: the figures are not net of a checked charge sheet");
        }

        if (figures.CagrPct is not { } cagr || !double.IsFinite(cagr))
        {
            return new(HoldOutStatus.Inconclusive, "unavailable: CAGR");
        }

        if (figures.MaxDrawdownPct is not { } drawdown)
        {
            return new(HoldOutStatus.Inconclusive, "unavailable: the maximum drawdown");
        }

        if (figures.ArmAnswer is not { } answer || !Answers.Contains(answer, StringComparer.Ordinal))
        {
            return new(HoldOutStatus.Inconclusive, "unavailable: the comparison with the no-selection baseline (" + (figures.ArmAnswer ?? "none") + ")");
        }

        var failed = new List<string>();
        if (cagr <= 0)
        {
            failed.Add(string.Create(CultureInfo.InvariantCulture, $"CAGR {cagr:0.0} % is not above 0"));
        }

        if ((double)drawdown > rule.MaxDrawdownPct)
        {
            failed.Add(string.Create(CultureInfo.InvariantCulture, $"maximum drawdown {drawdown:0.0} % is above the limit of {rule.MaxDrawdownPct:0.0} %"));
        }

        if (answer == BaselineBetter)
        {
            failed.Add("the no-selection baseline did better than the selection");
        }

        return failed.Count == 0
            ? new(HoldOutStatus.Pass, string.Create(CultureInfo.InvariantCulture, $"{trades} trades, CAGR {cagr:0.0} %, maximum drawdown {drawdown:0.0} % within {rule.MaxDrawdownPct:0.0} %, comparison {answer}"))
            : new(HoldOutStatus.Fail, string.Join("; ", failed));
    }
}
