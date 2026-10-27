using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Indicators;
using XDrishti.Infrastructure.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Infrastructure.Ai.Assistant;

/// <summary>
/// The grammar sheet of the strategy language, generated from code and not from prose (spec P9-07 D3): every feature of <see cref="IndicatorCatalog"/> with its arguments and defaults, the stop kinds, entry kinds and exit policies of the model, the time-rule keys, the rules the validator
/// enforces and the known gaps, then two examples copied from the seeded strategies. A new indicator therefore appears in the sheet by itself. Deterministic: the same code gives the same bytes and the same hash.
/// </summary>
internal sealed class DraftSheetBuilder : IDraftSheetSource
{
    private static readonly string[] ExampleNames = ["VWAP_PULLBACK.yaml", "BASE_BREAKOUT.yaml"];

    private readonly Lazy<string> _sheet = new(Build);

    public string Sheet => _sheet.Value;

    public string Hash => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(Sheet)));

    internal static string Build()
    {
        var b = new StringBuilder();
        b.Append("FILE SHAPE (YAML, one document, all keys under `strategy:`):\n");
        b.Append("schema: 1 | id: letters, digits, underscore, starting with a letter | style: intraday or swing | timeframes: [15m] (1m 5m 10m 15m 30m 1h 1D 1W 1M) | sides: [BUY, SELL]\n");
        b.Append("params: up to 4 named numbers, used by name in conditions and in numeric fields (`rr: [rr]`, `lookback: base_len`)\n");
        b.Append("setup: / trigger: each `all:` (every condition true) or `any:`, a list of quoted condition strings\n");
        b.Append("entry: {type: at_close}  (the only entry kind built today)\n");
        b.Append("stop: {type: <stop kind>, ...fields of that kind}   exits: [{policy: <exit policy>, ...}]   time_rules: {first_entry, last_entry, square_off as \"HH:MM\" (09:15 to 15:30), max_hold_sessions}\n");
        b.Append("sell: {setup, trigger, entry, stop, exits}  an explicit block for the SELL side. There is NO automatic mirror of the buy side: for shorts you must write the sell block yourself.\n");
        b.Append("Never write `version` (the server assigns it).\n\n");
        b.Append("CONDITIONS: comparisons and `and`/`or`/`not` over features, numbers, params and prices (open high low close volume vwap). `close[1]` is one bar back. `tf(\"1h\").ema(20)` reads a feature on another timeframe (only completed higher-timeframe bars are used). A value that is not ready yet is unknown, and a condition on an unknown value is not true.\n\n");
        b.Append("FEATURES (name(arguments), defaults in brackets):\n");
        foreach (var info in IndicatorCatalog.All.OrderBy(i => i.Name, StringComparer.Ordinal))
        {
            b.Append("- ").Append(info.Name);
            if (info.Parameters.Count > 0)
            {
                b.Append('(').Append(string.Join(", ", info.Parameters.Select(Parameter))).Append(')');
            }

            if (info.UsesSource)
            {
                b.Append(" [source=").Append(info.DefaultSource.ToString().ToLowerInvariant()).Append(']');
            }

            if (info.IntradayOnly)
            {
                b.Append(" (intraday timeframes only)");
            }

            b.Append(": ").Append(info.Summary).Append('\n');
        }

        b.Append("\nSTOP KINDS: ").Append(string.Join(", ", Enum.GetNames<StopKind>().Select(Snake))).Append("  (swing_low and swing_high take `lookback`; fixed_pct takes `pct`; atr_mult takes `mult` and `length`)\n");
        b.Append("ENTRY KINDS in the model: ").Append(string.Join(", ", Enum.GetNames<EntryKind>().Select(Snake))).Append(" (only at_close is built)\n");
        b.Append("EXIT POLICIES: ").Append(string.Join(", ", Enum.GetNames<ExitPolicy>().Select(s => Snake(s).ToUpperInvariant()))).Append(" (FIXED_RR takes `rr: [number or param]`; prefer it)\n");
        b.Append("TIME RULES: first_entry, last_entry, square_off (intraday), max_hold_sessions (swing).\n\n");
        b.Append("NOT AVAILABLE TODAY (decline, or list in not_expressed): news and events, results and estimates, fundamentals, sector relative strength, tick or depth data, options, a computed price level as a stop, more than one exit candidate per side, a buffer on swing stops, any data not stored (only OHLCV bars and the features above).\n\n");
        var seeds = StrategySeeder.EmbeddedTexts();
        foreach (var name in ExampleNames)
        {
            var text = seeds.FirstOrDefault(s => s.Name == name).Text;
            if (text is not null)
            {
                b.Append("EXAMPLE ").Append(name[..^5]).Append(":\n").Append(text.ReplaceLineEndings("\n").TrimEnd()).Append("\n\n");
            }
        }

        return b.ToString().TrimEnd();
    }

    private static string Parameter(IndicatorParameter p) => string.Create(CultureInfo.InvariantCulture, $"{p.Name}=[{p.Default}]");

    private static string Snake(string pascal)
    {
        var b = new StringBuilder();
        foreach (var c in pascal)
        {
            if (char.IsUpper(c) && b.Length > 0)
            {
                b.Append('_');
            }

            b.Append(char.ToLowerInvariant(c));
        }

        return b.ToString();
    }
}
