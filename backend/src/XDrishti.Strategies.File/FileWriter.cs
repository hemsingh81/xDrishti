using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Canonical;
using XDrishti.Strategies.Compilation;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.File;

/// <summary>
/// Writes a definition as YAML text in one fixed layout (P3-02c D7): keys in the order of <see cref="FileShape"/>, block style, two spaces, <c>\n</c> line ends and a trailing newline, no comments. The rules of the BUY side sit
/// directly under <c>strategy</c>, the SELL side under <c>sell:</c>. Conditions and times are double-quoted, other text is plain when it is a plain word. A side with no section at all is not written (it reads back as absent).
/// </summary>
internal static class FileWriter
{
    public static string Write(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var text = new StringBuilder();
        text.Append("strategy:\n");
        Pair(text, 1, "schema", Num(definition.Schema));
        Pair(text, 1, "id", Word(definition.Id));
        Pair(text, 1, "style", Word(StrategyNames.Styles.Name(definition.Style)));
        Scalars(text, 1, "timeframes", definition.Timeframes.Select(t => Word(t.ToCode())));
        Scalars(text, 1, "sides", definition.Sides.Select(s => Word(StrategyNames.Sides.Name(s))));
        if (definition.Params.Count > 0)
        {
            text.Append("  params:\n");
            foreach (var p in definition.Params)
            {
                Pair(text, 2, p.Name, Num(p.Value));
            }
        }

        if (definition.Buy is { } buy)
        {
            Side(text, 1, buy);
        }

        if (definition.Sell is { } sell)
        {
            if (sell == new SideRules(null, null, null, null, null, null, null))
            {
                text.Append("  sell: {}\n");
            }
            else
            {
                text.Append("  sell:\n");
                Side(text, 2, sell);
            }
        }

        if (definition.TimeRules is { } rules)
        {
            Rules(text, rules);
        }

        if (definition.Filters.Count > 0)
        {
            Scalars(text, 1, "filters", definition.Filters.Select(Word));
        }

        return text.ToString();
    }

    private static void Side(StringBuilder text, int level, SideRules rules)
    {
        Conditions(text, level, "setup", rules.Setup);
        Conditions(text, level, "trigger", rules.Trigger);
        Conditions(text, level, "invalidate", rules.Invalidate);
        if (rules.Entry is { } entry)
        {
            Line(text, level, "entry:");
            Pair(text, level + 1, "type", Word(StrategyNames.Entries.Name(entry.Kind)));
        }

        if (rules.Stop is { } stop)
        {
            Line(text, level, "stop:");
            Pair(text, level + 1, "type", Word(StrategyNames.Stops.Name(stop.Kind)));
            if (stop.Timeframe is { } frame)
            {
                Pair(text, level + 1, "timeframe", Word(frame.ToCode()));
            }

            Quantity(text, level + 1, "pct", stop.Pct);
            Quantity(text, level + 1, "mult", stop.Mult);
            Quantity(text, level + 1, "lookback", stop.Lookback);
            Quantity(text, level + 1, "minutes", stop.Minutes);
            Quantity(text, level + 1, "buffer_atr", stop.BufferAtr);
            Quantity(text, level + 1, "atr_period", stop.AtrPeriod);
        }

        if (rules.Exits is { } exits)
        {
            List(text, level, "exits", "policy", exits.Select(e => (StrategyNames.Policies.Name(e.Policy), e.Parameters, ExitParameterInfo.Of(e.Policy))));
        }

        if (rules.Overlays is { } overlays)
        {
            List(text, level, "overlays", "kind", overlays.Select(o => (StrategyNames.Overlays.Name(o.Kind), o.Parameters, ExitParameterInfo.Of(o.Kind))));
        }
    }

    private static void List(StringBuilder text, int level, string key, string kindKey, IEnumerable<(string Kind, IReadOnlyList<ExitParameter> Parameters, IReadOnlyList<ExitParameterInfo> Known)> items)
    {
        var all = items.ToList();
        if (all.Count == 0)
        {
            Pair(text, level, key, "[]");
            return;
        }

        Line(text, level, key + ":");
        foreach (var (kind, parameters, known) in all)
        {
            Line(text, level + 1, "- " + kindKey + ": " + Word(kind));
            foreach (var parameter in parameters)
            {
                var isList = known.FirstOrDefault(k => k.Name == parameter.Name)?.IsList ?? parameter.Values.Count != 1;
                var values = parameter.Values.Select(Value).ToList();
                Line(text, level + 2, parameter.Name + ": " + (isList || values.Count != 1 ? "[" + string.Join(", ", values) + "]" : values[0]));
            }
        }
    }

    private static void Conditions(StringBuilder text, int level, string key, ConditionList? list)
    {
        if (list is null)
        {
            return;
        }

        Line(text, level, key + ":");
        if (list.Items.Count == 0)
        {
            Pair(text, level + 1, StrategyNames.Modes.Name(list.Mode), "[]");
            return;
        }

        Line(text, level + 1, StrategyNames.Modes.Name(list.Mode) + ":");
        foreach (var item in list.Items)
        {
            Line(text, level + 2, "- " + Quote(item));
        }
    }

    private static void Rules(StringBuilder text, TimeRules rules)
    {
        if (rules == new TimeRules(null, null, null, null))
        {
            text.Append("  time_rules: {}\n");
            return;
        }

        text.Append("  time_rules:\n");
        Clock(text, "first_entry", rules.FirstEntry);
        Clock(text, "last_entry", rules.LastEntry);
        Clock(text, "square_off", rules.SquareOff);
        if (rules.MaxHoldSessions is { } hold)
        {
            Pair(text, 2, "max_hold_sessions", Num(hold));
        }
    }

    private static void Clock(StringBuilder text, string key, TimeOnly? time)
    {
        if (time is { } t)
        {
            Pair(text, 2, key, "\"" + t.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Minute.ToString("00", CultureInfo.InvariantCulture) + "\"");
        }
    }

    private static void Scalars(StringBuilder text, int level, string key, IEnumerable<string> values)
    {
        var all = values.ToList();
        if (all.Count == 0)
        {
            Pair(text, level, key, "[]");
            return;
        }

        Line(text, level, key + ":");
        foreach (var value in all)
        {
            Line(text, level + 1, "- " + value);
        }
    }

    private static void Quantity(StringBuilder text, int level, string key, Quantity? quantity)
    {
        if (quantity is { } q)
        {
            Pair(text, level, key, Value(q));
        }
    }

    private static string Value(Quantity q) => q.Param is { } name ? name : CanonicalText.Number(q.Number ?? 0);

    private static void Pair(StringBuilder text, int level, string key, string value) => Line(text, level, key + ": " + value);

    private static void Line(StringBuilder text, int level, string content) => text.Append(' ', level * 2).Append(content).Append('\n');

    private static string Num(double value) => CanonicalText.Number(value);

    private static string Num(int value) => CanonicalText.Number(value);

    /// <summary>A plain word when it is one (letters, digits and underscores, with at least one letter, so 15m and 1D stay plain and 15 does not), otherwise a double-quoted scalar.</summary>
    private static string Word(string value) => IsPlain(value) ? value : Quote(value);

    private static bool IsPlain(string value) =>
        value.Length > 0 && char.IsAsciiLetterOrDigit(value[0]) && value.Any(char.IsAsciiLetter) && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !FileShape.Reserved.Contains(value.ToLowerInvariant());

    /// <summary>A double-quoted YAML scalar: the quote, the backslash and the control characters are escaped.</summary>
    private static string Quote(string value)
    {
        var text = new StringBuilder(value.Length + 2).Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    text.Append("\\\"");
                    break;
                case '\\':
                    text.Append("\\\\");
                    break;
                case '\n':
                    text.Append("\\n");
                    break;
                case '\r':
                    text.Append("\\r");
                    break;
                case '\t':
                    text.Append("\\t");
                    break;
                default:
                    if (c < ' ' || c == '\u007f')
                    {
                        text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        text.Append(c);
                    }

                    break;
            }
        }

        return text.Append('"').ToString();
    }
}
