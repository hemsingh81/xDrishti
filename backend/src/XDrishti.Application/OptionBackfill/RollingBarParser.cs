using System.Text.Json;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Domain.Market;

namespace XDrishti.Application.OptionBackfill;

/// <summary>What a body made of: the outcome (<c>ok</c> or <c>bad_body</c>), the bars kept, the bars dropped for lying outside 09:15 to 15:30 India time and the bars dropped because a value was unusable.</summary>
public sealed record RollingParse(string Outcome, IReadOnlyList<BackfillBar> Bars, int OutsideHours, int Unusable)
{
    public static RollingParse BadBody { get; } = new(BackfillOutcomes.BadBody, [], 0, 0);
}

/// <summary>
/// The answer of <c>POST /v2/charts/rollingoption</c> to bars, pure and never throwing (P14-03 criterion 4): <c>data.ce</c> or <c>data.pe</c> (the one matching the request is used and the other must be null), each with parallel arrays
/// <c>timestamp, open, high, low, close, strike, spot</c> and, when sent, <c>volume, oi, iv</c>. Arrays of different lengths are <c>bad_body</c>. Prices and strikes become integer paise (half away from zero); a close of zero or less, or a strike that is not a
/// multiple of the underlying's strike step, is kept with the <c>suspect</c> flag; a value Dhan did not send is null, never zero; a bar outside 09:15 to 15:30 India time is dropped and counted. A null side is an empty window (zero bars), not an error.
/// <b>The shape, the timestamp zone (read as Unix seconds, or milliseconds from 13 digits) and the IV unit (stored as sent) are UNVERIFIED until the owner's probe result is recorded (ADR 0177).</b>
/// </summary>
public static class RollingBarParser
{
    /// <summary>More bars than 30 days of 5-minute sessions can hold; a bigger answer is unusable.</summary>
    public const int MaxBars = 20_000;

    private const double MillisecondsFrom = 100_000_000_000d;

    private static readonly TimeOnly SessionStart = new(9, 15);

    private static readonly TimeOnly SessionEnd = new(15, 30);

    public static RollingParse Parse(ReadOnlySpan<byte> body, BackfillItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            return Read(document.RootElement, item);
        }
        catch (JsonException)
        {
            return RollingParse.BadBody;
        }
    }

    private static RollingParse Read(JsonElement root, BackfillItem item)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data))
        {
            return RollingParse.BadBody;
        }

        if (data.ValueKind == JsonValueKind.Null)
        {
            return new RollingParse(BackfillOutcomes.Ok, [], 0, 0);
        }

        if (data.ValueKind != JsonValueKind.Object)
        {
            return RollingParse.BadBody;
        }

        var (ownName, otherName) = item.Key.Side == "CE" ? ("ce", "pe") : ("pe", "ce");
        if (data.TryGetProperty(otherName, out var other) && other.ValueKind != JsonValueKind.Null)
        {
            return RollingParse.BadBody;
        }

        if (!data.TryGetProperty(ownName, out var own) || own.ValueKind == JsonValueKind.Null)
        {
            return new RollingParse(BackfillOutcomes.Ok, [], 0, 0);
        }

        if (own.ValueKind != JsonValueKind.Object)
        {
            return RollingParse.BadBody;
        }

        if (!TryArray(own, "timestamp", true, out var timestamp) || !TryArray(own, "open", true, out var open) || !TryArray(own, "high", true, out var high) || !TryArray(own, "low", true, out var low)
            || !TryArray(own, "close", true, out var close) || !TryArray(own, "strike", true, out var strike) || !TryArray(own, "spot", true, out var spot)
            || !TryArray(own, "volume", false, out var volume) || !TryArray(own, "oi", false, out var oi) || !TryArray(own, "iv", false, out var iv))
        {
            return RollingParse.BadBody;
        }

        var length = timestamp.GetArrayLength();
        if (length > MaxBars || new[] { open, high, low, close, strike, spot }.Any(a => a.GetArrayLength() != length) || new[] { volume, oi, iv }.Any(a => a.ValueKind == JsonValueKind.Array && a.GetArrayLength() != length))
        {
            return RollingParse.BadBody;
        }

        var bars = new List<BackfillBar>(length);
        int outside = 0, unusable = 0;
        for (var i = 0; i < length; i++)
        {
            if (!Time(timestamp[i], out var ts))
            {
                unusable++;
                continue;
            }

            var clock = TimeOnly.FromTimeSpan(IndiaTime.ToIst(ts).TimeOfDay);
            if (clock < SessionStart || clock > SessionEnd)
            {
                outside++;
                continue;
            }

            if (!Paise(open[i], out var o) || !Paise(high[i], out var h) || !Paise(low[i], out var l) || !Paise(close[i], out var c) || !Paise(strike[i], out var k) || !Paise(spot[i], out var s))
            {
                unusable++;
                continue;
            }

            var flags = c <= 0 || (item.StrikeStepPaise is > 0 and var step && k % step != 0) ? BackfillFlags.Suspect : (short)0;
            bars.Add(new BackfillBar(ts, item.Key.UnderlyingId, item.Key.ExpiryFlag, item.Key.ExpiryCode, item.Key.Offset, item.Key.Side, item.Key.IntervalMinutes, k, s, o, h, l, c, Count(volume, i), Count(oi, i), Real(iv, i), flags));
        }

        return new RollingParse(BackfillOutcomes.Ok, bars, outside, unusable);
    }

    private static bool TryArray(JsonElement side, string name, bool required, out JsonElement array)
    {
        array = default;
        if (!side.TryGetProperty(name, out var found) || found.ValueKind == JsonValueKind.Null)
        {
            return !required;
        }

        array = found;
        return found.ValueKind == JsonValueKind.Array;
    }

    private static bool Time(JsonElement value, out DateTimeOffset time)
    {
        time = default;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var raw) || !double.IsFinite(raw) || raw <= 0)
        {
            return false;
        }

        var seconds = raw >= MillisecondsFrom ? raw / 1000d : raw;
        if (seconds > 253_402_300_799d)
        {
            return false;
        }

        time = DateTimeOffset.FromUnixTimeSeconds((long)Math.Round(seconds, MidpointRounding.AwayFromZero));
        return true;
    }

    private static bool Paise(JsonElement value, out int paise)
    {
        paise = 0;
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var rupees) || !double.IsFinite(rupees))
        {
            return false;
        }

        var scaled = Math.Round(rupees * 100d, MidpointRounding.AwayFromZero);
        if (scaled is > int.MaxValue or < int.MinValue)
        {
            return false;
        }

        paise = (int)scaled;
        return true;
    }

    private static long? Count(JsonElement array, int index)
    {
        if (array.ValueKind != JsonValueKind.Array || array[index].ValueKind != JsonValueKind.Number || !array[index].TryGetDouble(out var number) || !double.IsFinite(number) || number < 0 || number > long.MaxValue)
        {
            return null;
        }

        return (long)Math.Round(number, MidpointRounding.AwayFromZero);
    }

    private static float? Real(JsonElement array, int index)
    {
        if (array.ValueKind != JsonValueKind.Array || array[index].ValueKind != JsonValueKind.Number || !array[index].TryGetDouble(out var number) || !double.IsFinite(number))
        {
            return null;
        }

        var value = (float)number;
        return float.IsFinite(value) ? value : null;
    }
}
