using System.Globalization;
using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Assist;

/// <summary>The field-by-field comparison of the reader with the engine's values, with the tolerances of the protocol section 5: a price to the tick, a time to the minute (exact here), the reason exactly, R to 0.01.</summary>
internal static class FieldComparison
{
    private const decimal RTolerance = 0.01m;

    public static IReadOnlyList<string> Differences(ReaderReading reading, IReadOnlyDictionary<string, string> engine, decimal masterTick)
    {
        ArgumentNullException.ThrowIfNull(reading);
        ArgumentNullException.ThrowIfNull(engine);

        var tick = engine.TryGetValue("tick", out var t) && decimal.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) && parsed > 0 ? parsed : masterTick;
        var differing = new List<string>();
        Text(differing, "entry time", AssistLog.Time(reading.EntryEpoch), engine, "entry_ist");
        Price(differing, "entry price", reading.EntryPrice, engine, "entry_price", tick);
        Price(differing, "stop", reading.Stop, engine, "stop_price", tick);
        Price(differing, "target", reading.Target, engine, "target_price", tick);
        Text(differing, "exit time", AssistLog.Time(reading.ExitEpoch), engine, "exit_ist");
        Price(differing, "exit price", reading.ExitPrice, engine, "exit_price", tick);
        Text(differing, "exit reason", reading.ExitReason.Code(), engine, "exit_reason");
        Price(differing, "seen_r", reading.SeenR, engine, "r_exact", RTolerance);
        return differing;
    }

    private static void Text(List<string> differing, string name, string seen, IReadOnlyDictionary<string, string> engine, string column)
    {
        if (!engine.TryGetValue(column, out var wanted) || !string.Equals(seen, wanted, StringComparison.Ordinal))
        {
            differing.Add(name);
        }
    }

    private static void Price(List<string> differing, string name, decimal seen, IReadOnlyDictionary<string, string> engine, string column, decimal tolerance)
    {
        if (!engine.TryGetValue(column, out var wanted) || !decimal.TryParse(wanted, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || Math.Abs(seen - value) > tolerance)
        {
            differing.Add(name);
        }
    }
}
