using System.Globalization;
using System.Text;

namespace XDrishti.Application.Tests.OptionChain;

/// <summary>Constructed answers of Dhan's option-chain endpoints, in the documented shape (docs/research/dhan-samples/optionchain-*.json): <b>UNVERIFIED</b> until the pilot.</summary>
internal static class ChainSamples
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, Ist);

    /// <summary>One leg as the documentation shows it; a null argument leaves the field out.</summary>
    public static string Leg(long id = 42528, string last = "134", string bid = "133.55", string ask = "134", string iv = "9.789", string delta = "0.53871", string theta = "-15.1539", string gamma = "0.00132", string vega = "12.18593", string oi = "3786445")
    {
        var fields = new List<string?>
        {
            "\"average_price\":146.99",
            $"\"greeks\":{{\"delta\":{delta},\"theta\":{theta},\"gamma\":{gamma},\"vega\":{vega}}}",
            $"\"implied_volatility\":{iv}",
            $"\"last_price\":{last}",
            $"\"oi\":{oi}",
            "\"previous_close_price\":244.85",
            "\"previous_oi\":402220",
            "\"previous_volume\":31931705",
            $"\"security_id\":{id}",
            $"\"top_ask_price\":{ask}",
            "\"top_ask_quantity\":1365",
            $"\"top_bid_price\":{bid}",
            "\"top_bid_quantity\":1625",
            "\"volume\":117567970",
        };
        return "{" + string.Join(',', fields.Where(f => f is not null)) + "}";
    }

    /// <summary>A chain answer: the underlying's last price and the strikes with their legs (key as Dhan writes it).</summary>
    public static byte[] Chain(string underlying, params (string Strike, string? Ce, string? Pe)[] strikes)
    {
        var text = new StringBuilder();
        text.Append("{\"data\":{\"last_price\":").Append(underlying).Append(",\"oc\":{");
        text.Append(string.Join(',', strikes.Select(s => $"\"{s.Strike}\":{{" + string.Join(',', new[] { s.Ce is null ? null : $"\"ce\":{s.Ce}", s.Pe is null ? null : $"\"pe\":{s.Pe}" }.Where(x => x is not null)) + "}")));
        text.Append("}},\"status\":\"success\"}");
        return Encoding.UTF8.GetBytes(text.ToString());
    }

    /// <summary>Strikes 1000.00, 1100.00 ... for <paramref name="count"/> strikes around 25 000, each with both legs.</summary>
    public static byte[] Ladder(string underlying, int count, int firstStrike = 25_000)
    {
        var strikes = Enumerable.Range(0, count).Select(i => (((firstStrike + (i * 50)).ToString(CultureInfo.InvariantCulture)) + ".000000", (string?)Leg(100_000 + i), (string?)Leg(200_000 + i))).ToArray();
        return Chain(underlying, strikes);
    }

    public static byte[] Expiries(params string[] dates) => Encoding.UTF8.GetBytes("{\"data\":[" + string.Join(',', dates.Select(d => $"\"{d}\"")) + "],\"status\":\"success\"}");
}
