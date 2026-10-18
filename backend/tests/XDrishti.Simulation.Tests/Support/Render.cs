using System.Globalization;
using System.Text;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>A plain formatter (invariant culture, round-trip doubles) for candidates, traces and counters: what the determinism tests compare and what the golden file holds.</summary>
internal static class Render
{
    public static string Number(double value) => double.IsNaN(value) ? "NaN" : value.ToString("R", CultureInfo.InvariantCulture);

    public static string Instant(long epochSeconds) => DateTimeOffset.FromUnixTimeSeconds(epochSeconds).ToOffset(TimeSpan.FromMinutes(330)).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);

    public static string Candidate(EntryCandidate c, bool withTrace = true)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"candidate {c.StrategyId} {c.Instrument.Symbol} {c.Side} index={c.DecisionBarIndex} decision={Instant(c.DecisionEpochSeconds)} tf={c.TriggerTimeframe} stop={Number(c.StopLevel)} ");
        text.Append(CultureInfo.InvariantCulture, $"bar={Instant(c.TriggerBar.EpochSeconds)} O={Number(c.TriggerBar.Open)} H={Number(c.TriggerBar.High)} L={Number(c.TriggerBar.Low)} C={Number(c.TriggerBar.Close)} V={c.TriggerBar.Volume} minutes={c.TriggerBarMinutes} complete={c.TriggerBarComplete}");
        if (withTrace)
        {
            foreach (var item in c.Trace.Items)
            {
                text.Append(CultureInfo.InvariantCulture, $"\n  {item.Path} `{item.Source}` {item.Truth}");
                foreach (var value in item.Values)
                {
                    text.Append(CultureInfo.InvariantCulture, $"\n    {value.Feature} [{string.Join(", ", value.Lags.Select(Number))}]");
                }
            }
        }

        return text.ToString();
    }

    public static string Counters(ScanCounters s)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"counters {s.Side} bars={s.Bars} ignored={s.IgnoredBars} incomplete={s.IncompleteBarsConsumed} decisions={s.DecisionSteps} lost={s.LostDecisions} stale={s.StaleBucketSteps} ");
        text.Append(CultureInfo.InvariantCulture, $"outOfWindow={s.OutOfWindowSteps} window={s.WindowSteps} fullyReady={s.FullyReadySteps} candidates={s.Candidates} later={s.LaterTriggers} blocked={s.BlockedByInvalidation} ");
        text.Append(CultureInfo.InvariantCulture, $"invalidatedDays={s.InvalidatedDays} stopNotReady={s.StopNotReady} unknownInvalidation={s.UnknownInvalidation}");
        foreach (var condition in s.Conditions)
        {
            text.Append(CultureInfo.InvariantCulture, $"\n  {condition.Path} T={condition.True} F={condition.False} U={condition.Unknown} N={condition.NotReady}");
        }

        return text.ToString();
    }

    /// <summary>The candidates (with traces) and the counters of a scanner.</summary>
    public static string Scanner(EntryScanner scanner) =>
        string.Join("\n", scanner.Candidates.Select(c => Candidate(c))) + (scanner.Candidates.Count > 0 ? "\n" : string.Empty) + Counters(scanner.Counters);
}
