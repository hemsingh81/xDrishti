using System.Globalization;
using System.Text;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;
using XDrishti.Strategies.Syntax;

namespace XDrishti.Strategies.Canonical;

/// <summary>
/// The canonical JSON of a strategy definition (spec P3-04, Scope 2; P3-02c D8, ADR 0055). It is taken from the model, never from the YAML. <c>params</c> and the parameters of an exit or overlay are written sorted by name (ordinal), because a YAML mapping has no order. Hand-written over a <see cref="StringBuilder"/>, not <c>System.Text.Json</c>: the bytes are ours and not a framework
/// version's. Member names are the snake_case of the model's property names; <c>Side</c> and exit policies are written as doc 11 spells them (<c>BUY</c>, <c>SELL</c>, <c>FIXED_RR</c>, <c>PARTIAL_TRAIL</c>), entry, stop, overlay and
/// style kinds and list modes in snake_case (<c>at_close</c>, <c>swing_low</c>, <c>time_stop</c>, <c>intraday</c>, <c>all</c>), timeframes by their code (<c>15m</c>); a <see cref="Quantity"/> is a JSON number or the parameter name as a
/// string; times are <c>HH:mm</c>; numbers are written by <see cref="CanonicalText"/>; strings are UTF-8 with only <c>"</c>, <c>\</c> and control characters escaped (<c>&lt;</c>, <c>&gt;</c>, <c>&amp;</c> and <c>'</c> stay as written); no
/// white space; absent members are left out; lists keep the order of the model. Each condition is parsed and printed again (<see cref="ExpressionPrinter"/>), so white space and redundant brackets do not change the text; two spellings of
/// one feature (<c>rsi()</c> and <c>rsi(14)</c>) do, because the text is the version (ADR 0002).
/// </summary>
public static class CanonicalStrategy
{
    /// <summary>The canonical text of the definition.</summary>
    public static string Json(StrategyDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        return Obj(
            ("schema", Num(definition.Schema)),
            ("id", Str(definition.Id)),
            ("style", Str(StrategyNames.Styles.Name(definition.Style))),
            ("timeframes", definition.Timeframes is { } frames ? Arr(frames.Select(f => Str(f.ToCode()))) : null),
            ("sides", definition.Sides is { } sides ? Arr(sides.Select(s => Str(StrategyNames.Sides.Name(s)))) : null),
            ("params", definition.Params is { } parameters ? Arr(parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Obj(("name", Str(p.Name)), ("value", Num(p.Value))))) : null),
            ("buy", definition.Buy is { } buy ? SideJson(buy) : null),
            ("sell", definition.Sell is { } sell ? SideJson(sell) : null),
            ("time_rules", definition.TimeRules is { } rules ? Rules(rules) : null),
            ("filters", definition.Filters is { } filters ? Arr(filters.Select(Str)) : null));
    }

    private static string SideJson(SideRules rules) => Obj(
        ("setup", rules.Setup is { } setup ? Conditions(setup) : null),
        ("trigger", rules.Trigger is { } trigger ? Conditions(trigger) : null),
        ("invalidate", rules.Invalidate is { } invalidate ? Conditions(invalidate) : null),
        ("entry", rules.Entry is { } entry ? Obj(("kind", Str(StrategyNames.Entries.Name(entry.Kind)))) : null),
        ("stop", rules.Stop is { } stop ? Stop(stop) : null),
        ("exits", rules.Exits is { } exits ? Arr(exits.Select(e => Obj(("policy", Str(StrategyNames.Policies.Name(e.Policy))), ("parameters", Parameters(e.Parameters))))) : null),
        ("overlays", rules.Overlays is { } overlays ? Arr(overlays.Select(o => Obj(("kind", Str(StrategyNames.Overlays.Name(o.Kind))), ("parameters", Parameters(o.Parameters))))) : null));

    private static string Conditions(ConditionList list) => Obj(
        ("mode", Str(StrategyNames.Modes.Name(list.Mode))),
        ("items", list.Items is { } items ? Arr(items.Select(i => Str(Condition(i)))) : null));

    private static string Condition(string source)
    {
        // A condition that does not parse cannot have been compiled (the side is not listed, or the definition is refused elsewhere): it is written as it stands.
        var parsed = ConditionParser.Parse(source);
        return parsed.Tree is { } tree ? ExpressionPrinter.Print(tree) : source;
    }

    private static string Stop(StopSpec stop) => Obj(
        ("kind", Str(StrategyNames.Stops.Name(stop.Kind))),
        ("timeframe", stop.Timeframe is { } frame ? Str(frame.ToCode()) : null),
        ("pct", Qty(stop.Pct)),
        ("mult", Qty(stop.Mult)),
        ("lookback", Qty(stop.Lookback)),
        ("minutes", Qty(stop.Minutes)),
        ("buffer_atr", Qty(stop.BufferAtr)),
        ("atr_period", Qty(stop.AtrPeriod)));

    private static string Parameters(IReadOnlyList<ExitParameter>? parameters) =>
        parameters is null ? "[]" : Arr(parameters.OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => Obj(("name", Str(p.Name)), ("values", p.Values is { } values ? Arr(values.Select(v => Qty(v) ?? "null")) : null))));

    private static string Rules(TimeRules rules) => Obj(
        ("first_entry", Time(rules.FirstEntry)),
        ("last_entry", Time(rules.LastEntry)),
        ("square_off", Time(rules.SquareOff)),
        ("max_hold_sessions", rules.MaxHoldSessions is { } hold ? Num(hold) : null));

    private static string? Time(TimeOnly? time) => time is { } t ? Str(t.Hour.ToString("00", CultureInfo.InvariantCulture) + ":" + t.Minute.ToString("00", CultureInfo.InvariantCulture)) : null;

    private static string? Qty(Quantity? quantity) =>
        quantity is not { } q ? null : q.Param is { } param ? Str(param) : q.Number is { } number ? Num(number) : null;

    private static string Obj(params (string Name, string? Json)[] members) =>
        "{" + string.Join(',', members.Where(m => m.Json is not null).Select(m => Str(m.Name) + ":" + m.Json)) + "}";

    private static string Arr(IEnumerable<string> items) => "[" + string.Join(',', items) + "]";

    private static string Num(double value) => CanonicalText.Number(value);

    private static string Num(int value) => CanonicalText.Number(value);

    /// <summary>A JSON string: only the double quote, the backslash and the control characters are escaped.</summary>
    private static string Str(string value)
    {
        var text = new StringBuilder(value.Length + 2);
        text.Append('"');
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
                case '\b':
                    text.Append("\\b");
                    break;
                case '\f':
                    text.Append("\\f");
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
                    if (c < ' ')
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
