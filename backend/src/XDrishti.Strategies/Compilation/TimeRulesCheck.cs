using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Compilation;

/// <summary>
/// The time rules (spec P3-02b, <i>Contract</i>): the one place for windows. Checks each rule against the style and the evaluation timeframe, the order <c>first_entry ≤ last_entry &lt; square_off</c>, and returns the rules
/// as minutes of the India day. Times are 09:15 to 15:30 on the minute (the range of the <c>time</c> keyword); nothing is read from configuration, so the same stored text always means the same behaviour.
/// </summary>
internal sealed class TimeRulesCheck(ProblemSink sink)
{
    private const int SessionOpenMinute = 9 * 60 + 15;

    private const int SessionCloseMinute = 15 * 60 + 30;

    private const int MinMaxHold = 1;

    private const int MaxMaxHold = 5;

    public SessionRules Check(StrategyDefinition definition, Timeframe evaluationTimeframe)
    {
        var rules = definition.TimeRules;
        var styleKnown = Enum.IsDefined(definition.Style);
        var onDaily = evaluationTimeframe is Timeframe.D1 or Timeframe.W1 or Timeframe.Mo1;
        var first = ReadTime(rules?.FirstEntry, "time_rules.first_entry");
        var last = ReadTime(rules?.LastEntry, "time_rules.last_entry");
        var squareOff = ReadTime(rules?.SquareOff, "time_rules.square_off");
        if (onDaily && rules?.FirstEntry is not null)
        {
            sink.Error("time_rules.first_entry", ModelCodes.TimeRuleOnDaily, "A " + evaluationTimeframe.ToCode() + " bar has no time of day, so `first_entry` cannot apply. Leave it out.");
        }

        if (onDaily && rules?.LastEntry is not null)
        {
            sink.Error("time_rules.last_entry", ModelCodes.TimeRuleOnDaily, "A " + evaluationTimeframe.ToCode() + " bar has no time of day, so `last_entry` cannot apply. Leave it out.");
        }

        if (styleKnown && definition.Style == Style.Intraday)
        {
            if (rules?.SquareOff is null)
            {
                sink.Error("time_rules.square_off", ModelCodes.TimeRuleMissing, "An intraday strategy needs `square_off`: the session exit is always on (doc 08 §2), and its time is part of the strategy.");
            }

            if (rules?.MaxHoldSessions is not null)
            {
                sink.Error("time_rules.max_hold_sessions", ModelCodes.MaxHoldOnIntraday, "`max_hold_sessions` is for swing strategies; an intraday strategy ends at `square_off`.");
            }
        }
        else if (styleKnown)
        {
            if (rules?.SquareOff is not null)
            {
                sink.Error("time_rules.square_off", ModelCodes.SquareOffOnSwing, "`square_off` is for intraday strategies; a swing strategy is held for sessions (`max_hold_sessions`).");
            }

            if (rules?.MaxHoldSessions is not { } hold)
            {
                sink.Error("time_rules.max_hold_sessions", ModelCodes.TimeRuleMissing, "A swing strategy needs `max_hold_sessions`: the longest hold is always on (doc 08 §2).");
            }
            else if (hold is < MinMaxHold or > MaxMaxHold)
            {
                sink.Error("time_rules.max_hold_sessions", ModelCodes.MaxHoldOutOfRange, "`max_hold_sessions` is " + hold + ", but it must be " + MinMaxHold + " to " + MaxMaxHold + ".");
            }
        }

        CheckOrder(first, last, squareOff);
        return new SessionRules(first, last, squareOff, rules?.MaxHoldSessions);
    }

    /// <summary>The minute of the day of a time, or null when it is absent or reported as <c>bad_time</c>.</summary>
    private int? ReadTime(TimeOnly? time, string path)
    {
        if (time is not { } value)
        {
            return null;
        }

        var minute = (int)(value.Ticks / TimeSpan.TicksPerMinute);
        if (value.Ticks % TimeSpan.TicksPerMinute != 0 || minute < SessionOpenMinute || minute > SessionCloseMinute)
        {
            sink.Error(path, ModelCodes.BadTime, "The time " + value.ToString("HH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture).TrimEnd('.') + " is not a minute of the session: write HH:MM from 09:15 to 15:30.");
            return null;
        }

        return minute;
    }

    private void CheckOrder(int? first, int? last, int? squareOff)
    {
        if (first is { } f && last is { } l && f > l)
        {
            sink.Error("time_rules.last_entry", ModelCodes.TimeRulesOutOfOrder, "`last_entry` " + Clock(l) + " is before `first_entry` " + Clock(f) + ".");
        }

        if (last is { } l2 && squareOff is { } s && l2 >= s)
        {
            sink.Error("time_rules.square_off", ModelCodes.TimeRulesOutOfOrder, "`square_off` " + Clock(s) + " must be after `last_entry` " + Clock(l2) + ".");
        }
        else if (last is null && first is { } f2 && squareOff is { } s2 && f2 >= s2)
        {
            sink.Error("time_rules.square_off", ModelCodes.TimeRulesOutOfOrder, "`square_off` " + Clock(s2) + " must be after `first_entry` " + Clock(f2) + ".");
        }
    }

    private static string Clock(int minute) => (minute / 60).ToString("00", CultureInfo.InvariantCulture) + ":" + (minute % 60).ToString("00", CultureInfo.InvariantCulture);
}
