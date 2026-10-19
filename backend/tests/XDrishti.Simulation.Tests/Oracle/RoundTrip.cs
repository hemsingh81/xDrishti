using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>
/// The round trip of D3, the test of the adapter on its own: a case taken through the adapter and back must come out equal to the original, field by field. Two directions: an engine case (bars, candidates, rules) through the oracle's records
/// and back to the engine's, and an oracle case through the engine's types and back. What an adapter drops, or normalises, would hollow the comparison without any test noticing; this is where it shows. It compares only what each
/// side carries: the echo fields of a candidate that the oracle has no field for are not part of it (the comparison reads them back as a pass-through, see <see cref="OracleAdapter.Walk"/>).
/// </summary>
internal static class RoundTrip
{
    /// <summary>Engine to oracle and back: the differences between the original and what comes back.</summary>
    internal static List<string> Engine(IReadOnlyList<Bar> bars, IReadOnlyList<EntryCandidate> candidates, WalkRules rules, Func<OracleRules, OracleRules>? lossy = null)
    {
        var oracleBars = OracleAdapter.Bars(bars);
        var oracleCandidates = candidates.Select(OracleAdapter.Candidate).ToList();
        var oracleRules = OracleAdapter.Rules(rules, bars);
        if (lossy is not null)
        {
            oracleRules = lossy(oracleRules);
        }

        var diff = new List<string>();
        var backBars = OracleAdapter.Bars(oracleBars);
        if (!backBars.SequenceEqual(bars))
        {
            diff.Add("bars");
        }

        var backCandidates = oracleCandidates.Select(OracleAdapter.Candidate).ToList();
        for (var i = 0; i < candidates.Count; i++)
        {
            var (a, b) = (candidates[i], backCandidates[i]);
            if (a.Side != b.Side || a.DecisionBarIndex != b.DecisionBarIndex || a.DecisionEpochSeconds != b.DecisionEpochSeconds
                || BitConverter.DoubleToInt64Bits(a.TriggerBar.Close) != BitConverter.DoubleToInt64Bits(b.TriggerBar.Close) || BitConverter.DoubleToInt64Bits(a.StopLevel) != BitConverter.DoubleToInt64Bits(b.StopLevel))
            {
                diff.Add("candidate " + i);
            }
        }

        diff.AddRange(Rules(rules, OracleAdapter.Rules(oracleRules), bars, "rules"));
        return diff;
    }

    /// <summary>Oracle to engine and back.</summary>
    internal static List<string> Oracle(OracleCase source, Func<OracleCandidate, OracleCandidate>? lossyCandidate = null, Func<OracleBar, OracleBar>? lossyBar = null)
    {
        var bars = OracleAdapter.Bars(source.Bars);
        var candidates = source.Candidates.Select(OracleAdapter.Candidate).ToList();
        var rules = OracleAdapter.Rules(source.Rules);
        var backBars = OracleAdapter.Bars(bars);
        if (lossyBar is not null)
        {
            backBars = [.. backBars.Select(lossyBar)];
        }

        var diff = new List<string>();
        if (!backBars.SequenceEqual(source.Bars))
        {
            diff.Add("bars");
        }

        for (var i = 0; i < candidates.Count; i++)
        {
            var back = OracleAdapter.Candidate(candidates[i]);
            if (lossyCandidate is not null)
            {
                back = lossyCandidate(back);
            }

            if (back != source.Candidates[i])
            {
                diff.Add("candidate " + i);
            }
        }

        var backRules = OracleAdapter.Rules(rules, bars);
        diff.AddRange(Rules(source.Rules, backRules, "rules"));
        return diff;
    }

    private static IEnumerable<string> Rules(WalkRules original, WalkRules rebuilt, IReadOnlyList<Bar> bars, string label)
    {
        if (original.RewardRisk != rebuilt.RewardRisk) { yield return label + " reward-risk"; }
        if (original.SquareOffMinute != rebuilt.SquareOffMinute) { yield return label + " square-off"; }
        if (BitConverter.DoubleToInt64Bits(original.Fills.EntryLimitPercent) != BitConverter.DoubleToInt64Bits(rebuilt.Fills.EntryLimitPercent)) { yield return label + " entry limit"; }
        if (original.Fills.Slippage.Ticks != rebuilt.Fills.Slippage.Ticks) { yield return label + " slippage ticks"; }
        if (original.Fills.Slippage.BarRangeFrac != rebuilt.Fills.Slippage.BarRangeFrac) { yield return label + " slippage range"; }
        if (original.Fills.Slippage.StopExitExtraTicks != rebuilt.Fills.Slippage.StopExitExtraTicks) { yield return label + " slippage stop ticks"; }
        if (original.Tick.Size != rebuilt.Tick.Size) { yield return label + " tick"; }
        if ((original.Hold is null) != (rebuilt.Hold is null)) { yield return label + " hold"; yield break; }
        if (original.Hold is { } a && rebuilt.Hold is { } b)
        {
            if (a.MaxHoldSessions != b.MaxHoldSessions) { yield return label + " hold sessions"; }
            var first = bars.Count == 0 ? new DateOnly(2026, 3, 2) : SessionClock.DateOf(bars[0].EpochSeconds);
            var last = bars.Count == 0 ? first : SessionClock.DateOf(bars[^1].EpochSeconds);
            var calendarA = a.Calendar ?? new XDrishti.Domain.Market.Calendar.TradingCalendar();
            var calendarB = b.Calendar ?? new XDrishti.Domain.Market.Calendar.TradingCalendar();
            for (var date = first.AddDays(-7); date <= last.AddDays(45); date = date.AddDays(1))
            {
                if (calendarA.SessionOf(date) != calendarB.SessionOf(date))
                {
                    yield return label + " calendar " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    yield break;
                }
            }
        }
    }

    private static IEnumerable<string> Rules(OracleRules original, OracleRules rebuilt, string label)
    {
        if (original.RewardRisk != rebuilt.RewardRisk) { yield return label + " reward-risk"; }
        if (original.SquareOffMinute != rebuilt.SquareOffMinute) { yield return label + " square-off"; }
        if (BitConverter.DoubleToInt64Bits(original.EntryLimitPercent) != BitConverter.DoubleToInt64Bits(rebuilt.EntryLimitPercent)) { yield return label + " entry limit"; }
        if (original.Slippage != rebuilt.Slippage) { yield return label + " slippage"; }
        if (original.Tick != rebuilt.Tick) { yield return label + " tick"; }
        if ((original.Hold is null) != (rebuilt.Hold is null)) { yield return label + " hold"; yield break; }
        if (original.Hold is { } a && rebuilt.Hold is { } b)
        {
            if (a.MaxHoldSessions != b.MaxHoldSessions) { yield return label + " hold sessions"; }
            var calendarA = a.Calendar ?? OracleCalendar.Default;
            var calendarB = b.Calendar ?? OracleCalendar.Default;
            for (var date = new DateOnly(2026, 1, 1); date <= new DateOnly(2027, 12, 31); date = date.AddDays(1))
            {
                if (calendarA.IsTradingDay(date) != calendarB.IsTradingDay(date) || (calendarA.IsTradingDay(date) && calendarA.SessionOf(date) != calendarB.SessionOf(date)))
                {
                    yield return label + " calendar " + date.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
                    yield break;
                }
            }
        }
    }
}
