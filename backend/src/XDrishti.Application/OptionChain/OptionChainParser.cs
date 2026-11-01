using System.Globalization;
using System.Text.Json;
using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Application.OptionChain;

/// <summary>How a chain body was read.</summary>
internal enum ChainParseStatus
{
    /// <summary>At least one row.</summary>
    Ok,

    /// <summary>Not the documented shape (counted as one gap, <c>bad_body</c>).</summary>
    BadBody,

    /// <summary>The shape is right and the chain holds no strike (<c>no_data</c>).</summary>
    NoData,
}

internal sealed record ChainParseResult(ChainParseStatus Status, IReadOnlyList<ChainSnapshotRow> Rows)
{
    public static ChainParseResult Bad { get; } = new(ChainParseStatus.BadBody, []);
}

/// <summary>
/// Pure reader of Dhan's option-chain answers (P14-02, <b>shape UNVERIFIED</b> until the pilot; written from <c>docs/research/dhan-samples/optionchain-*.json</c>). Never throws: a body that is not the documented shape is <see cref="ChainParseStatus.BadBody"/>.
/// Strike keys such as <c>"25650.000000"</c> become integer paise (half away from zero), prices become paise. A zero or missing bid/ask/last price is null with a flag - never a price; an unusable IV or Greek is null with <see cref="ChainFlags.BadGreeks"/>;
/// a bid above the ask is flagged, both kept. Only the strikes within <c>strikesEachSide</c> strikes of the one nearest the underlying's last price are kept.
/// </summary>
internal static class OptionChainParser
{
    private const decimal SanePrice = 10_000_000m;

    public static ChainParseResult ParseChain(ReadOnlySpan<byte> body, Guid underlyingId, DateOnly expiry, DateTimeOffset slotTs, DateTimeOffset rxAt, int strikesEachSide)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            return Read(document.RootElement, underlyingId, expiry, slotTs, rxAt, strikesEachSide);
        }
        catch (JsonException)
        {
            return ChainParseResult.Bad;
        }
    }

    /// <summary>The expiry dates of an expiry-list answer (<c>data</c> is an array of <c>yyyy-MM-dd</c>), ascending and distinct; null when the body is not that shape.</summary>
    public static IReadOnlyList<DateOnly>? ParseExpiries(ReadOnlySpan<byte> body)
    {
        try
        {
            using var document = JsonDocument.Parse(body.ToArray());
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var dates = new SortedSet<DateOnly>();
            foreach (var item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || !DateOnly.TryParseExact(item.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    return null;
                }

                dates.Add(date);
            }

            return [.. dates];
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static ChainParseResult Read(JsonElement root, Guid underlyingId, DateOnly expiry, DateTimeOffset slotTs, DateTimeOffset rxAt, int strikesEachSide)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("oc", out var oc) || oc.ValueKind != JsonValueKind.Object
            || !data.TryGetProperty("last_price", out var last) || Paise(last) is not { } underlying || underlying <= 0)
        {
            return ChainParseResult.Bad;
        }

        var strikes = new SortedDictionary<int, JsonElement>();
        foreach (var entry in oc.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.Object && StrikePaise(entry.Name) is { } strike)
            {
                strikes[strike] = entry.Value;
            }
        }

        if (strikes.Count == 0)
        {
            return oc.EnumerateObject().Any() ? ChainParseResult.Bad : new ChainParseResult(ChainParseStatus.NoData, []);
        }

        var keys = strikes.Keys.ToList();
        var nearest = 0;
        for (var i = 1; i < keys.Count; i++)
        {
            if (Math.Abs((long)keys[i] - underlying) < Math.Abs((long)keys[nearest] - underlying))
            {
                nearest = i;
            }
        }

        var rows = new List<ChainSnapshotRow>();
        for (var i = Math.Max(0, nearest - strikesEachSide); i <= Math.Min(keys.Count - 1, nearest + strikesEachSide); i++)
        {
            foreach (var (name, side) in new[] { ("ce", "CE"), ("pe", "PE") })
            {
                if (strikes[keys[i]].TryGetProperty(name, out var leg) && leg.ValueKind == JsonValueKind.Object && Leg(leg, underlyingId, expiry, slotTs, rxAt, underlying, keys[i], side) is { } row)
                {
                    rows.Add(row);
                }
            }
        }

        return rows.Count == 0 ? new ChainParseResult(ChainParseStatus.NoData, []) : new ChainParseResult(ChainParseStatus.Ok, rows);
    }

    private static ChainSnapshotRow? Leg(JsonElement leg, Guid underlyingId, DateOnly expiry, DateTimeOffset slotTs, DateTimeOffset rxAt, int underlying, int strike, string side)
    {
        if (!leg.TryGetProperty("security_id", out var id) || id.ValueKind != JsonValueKind.Number || !id.TryGetInt64(out var securityId) || securityId <= 0)
        {
            return null;
        }

        short flags = 0;
        var lastPx = Positive(leg, "last_price");
        if (lastPx is null)
        {
            flags |= ChainFlags.NoTrade;
        }

        var bid = Positive(leg, "top_bid_price");
        var ask = Positive(leg, "top_ask_price");
        if (bid is null)
        {
            flags |= ChainFlags.NoBid;
        }

        if (ask is null)
        {
            flags |= ChainFlags.NoAsk;
        }

        if (bid is { } b && ask is { } a && b > a)
        {
            flags |= ChainFlags.Crossed;
        }

        float? iv = null, delta = null, gamma = null, theta = null, vega = null;
        var bad = false;
        iv = Number(leg, "implied_volatility", ref bad, min: 0, openMin: true);
        if (leg.TryGetProperty("greeks", out var greeks) && greeks.ValueKind == JsonValueKind.Object)
        {
            delta = Number(greeks, "delta", ref bad, -1, false, 1);
            gamma = Number(greeks, "gamma", ref bad, 0, false);
            theta = Number(greeks, "theta", ref bad);
            vega = Number(greeks, "vega", ref bad, 0, false);
        }

        if (bad)
        {
            flags |= ChainFlags.BadGreeks;
        }

        return new ChainSnapshotRow(
            slotTs, underlyingId, expiry, strike, side, rxAt, securityId, underlying,
            lastPx, Positive(leg, "average_price"), Positive(leg, "previous_close_price"),
            bid, bid is null ? null : Count(leg, "top_bid_quantity"), ask, ask is null ? null : Count(leg, "top_ask_quantity"),
            Whole(leg, "oi"), Whole(leg, "previous_oi"), Whole(leg, "volume"), Whole(leg, "previous_volume"),
            iv, delta, gamma, theta, vega, flags);
    }

    /// <summary>A strike key as integer paise (half away from zero); null when it is not a positive plain decimal that fits.</summary>
    internal static int? StrikePaise(string key) =>
        decimal.TryParse(key, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && value > 0 && value < SanePrice ? ToPaise(value) : null;

    private static int? ToPaise(decimal rupees)
    {
        var paise = Math.Round(rupees * 100m, 0, MidpointRounding.AwayFromZero);
        return paise is > 0 and <= int.MaxValue ? (int)paise : null;
    }

    private static int? Paise(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number) && number > 0 && number < SanePrice ? ToPaise(number) : null;

    /// <summary>A positive price in paise; zero, negative, missing and non-numeric are null.</summary>
    private static int? Positive(JsonElement owner, string name) => owner.TryGetProperty(name, out var value) ? Paise(value) : null;

    private static int? Count(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number is >= 0 and <= int.MaxValue ? (int)number : null;

    private static long? Whole(JsonElement owner, string name) =>
        owner.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number) && number >= 0 ? number : null;

    /// <summary>A real number within the bounds; a missing field is null without a flag, a present one that is not a usable number (text, NaN, infinity, out of bounds) is null and sets <paramref name="bad"/>.</summary>
    private static float? Number(JsonElement owner, string name, ref bool bad, double min = double.NegativeInfinity, bool openMin = false, double max = double.PositiveInfinity)
    {
        if (!owner.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (value.ValueKind != JsonValueKind.Number || !value.TryGetDouble(out var number) || !double.IsFinite(number)
            || number < min || number > max || (openMin && number <= min) || !float.IsFinite((float)number))
        {
            bad = true;
            return null;
        }

        return (float)number;
    }
}
