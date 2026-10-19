using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The relations of the exit tables (spec P3-02b2, D11): what the range of one value cannot say because it compares two values of a policy, or a value with a fact from outside the policy. A relation is checked only among
/// the values that compiled (a value that already has a problem is absent from <c>values</c>), and a broken relation is <c>value_out_of_range</c> at the value that breaks it (<c>field_shape</c> for a list of the wrong length).
/// </summary>
internal static class ExitRelations
{
    /// <summary>Checks the relations of a candidate policy.</summary>
    public static void Check(ProblemSink sink, ExitPolicy policy, IReadOnlyDictionary<string, IReadOnlyList<double>> values, string path)
    {
        switch (policy)
        {
            case ExitPolicy.PartialBe:
                PartialBe(sink, values, path);
                break;
            case ExitPolicy.Pyramid:
                Pyramid(sink, values, path);
                break;
        }
    }

    /// <summary>Checks the relations of an overlay.</summary>
    public static void Check(ProblemSink sink, OverlayKind kind, IReadOnlyDictionary<string, IReadOnlyList<double>> values, ExitContext context, string path)
    {
        if (kind != OverlayKind.TimeStop)
        {
            return;
        }

        if (values.TryGetValue("intraday_bars", out var bars) && context.EvaluationTimeframe.Minutes() is var minutes and > 0)
        {
            var most = (ExitTables.SessionMinutes + minutes - 1) / minutes;
            if (bars[0] > most)
            {
                sink.Error(path + ".intraday_bars", ModelCodes.ValueOutOfRange, "`intraday_bars` is " + Format(bars[0]) + ", but a session of " + ExitTables.SessionMinutes + " minutes has " + most + " bars of " + context.EvaluationTimeframe.ToCode() + ".");
            }
        }

        if (values.TryGetValue("swing_sessions", out var sessions) && context.MaxHoldSessions is { } hold and >= 1 and <= ExitTables.MaxSwingSessions && sessions[0] >= hold)
        {
            sink.Error(
                path + ".swing_sessions",
                ModelCodes.ValueOutOfRange,
                hold == 1
                    ? "`swing_sessions` cannot be set: the strategy holds one session (`max_hold_sessions` 1), so there is nothing to time out."
                    : "`swing_sessions` is " + Format(sessions[0]) + ", but it must be below `max_hold_sessions` " + hold + ", or the time stop could never fire before the longest hold leaves.");
        }
    }

    private static void PartialBe(ProblemSink sink, IReadOnlyDictionary<string, IReadOnlyList<double>> values, string path)
    {
        if (!values.TryGetValue("t1_rr", out var t1) || !values.TryGetValue("t2_rr", out var t2))
        {
            return;
        }

        for (var j = 0; j < t2.Count; j++)
        {
            if (t2[j] <= t1[0])
            {
                sink.Error(ModelChecks.Index(path + ".t2_rr", j), ModelCodes.ValueOutOfRange, "`t2_rr` is " + Format(t2[j]) + ", but it must be above `t1_rr` " + Format(t1[0]) + ".");
            }
        }
    }

    private static void Pyramid(ProblemSink sink, IReadOnlyDictionary<string, IReadOnlyList<double>> values, string path)
    {
        var hasLevels = values.TryGetValue("add_at_rr", out var levels);
        if (hasLevels)
        {
            for (var j = 1; j < levels!.Count; j++)
            {
                if (levels[j] <= levels[j - 1])
                {
                    sink.Error(ModelChecks.Index(path + ".add_at_rr", j), ModelCodes.ValueOutOfRange, "`add_at_rr` is " + Format(levels[j]) + ", but the levels must increase and the one before is " + Format(levels[j - 1]) + ".");
                }
            }
        }

        if (hasLevels && values.TryGetValue("add_pct", out var pct))
        {
            if (pct.Count != levels!.Count)
            {
                sink.Error(path + ".add_pct", ModelCodes.FieldShape, "`add_pct` needs one value for each of the " + levels.Count + " values of `add_at_rr`, but " + pct.Count + (pct.Count == 1 ? " is" : " are") + " given.");
            }
            else if (values.TryGetValue("initial_pct", out var initial))
            {
                var sum = initial[0] + pct.Sum();
                if (sum > 100 + 1e-9)
                {
                    sink.Error(path + ".add_pct", ModelCodes.ValueOutOfRange, "`initial_pct` plus all of `add_pct` is " + Format(sum) + ", but it must be at most 100.");
                }
            }
        }

        if (hasLevels && values.TryGetValue("max_adds", out var adds) && adds[0] > levels!.Count)
        {
            sink.Error(path + ".max_adds", ModelCodes.ValueOutOfRange, "`max_adds` is " + Format(adds[0]) + ", but `add_at_rr` has only " + levels.Count + " level" + (levels.Count == 1 ? string.Empty : "s") + ".");
        }
    }

    private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);
}
