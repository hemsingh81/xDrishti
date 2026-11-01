using System.Globalization;
using System.Text;
using XDrishti.Application.Abstractions.OptionBackfill;
using XDrishti.Application.OptionBackfill;

namespace XDrishti.Application.Tests.OptionBackfill;

/// <summary>Constructed answers of <c>POST /v2/charts/rollingoption</c> in the documented shape (docs/research/dhan-samples/rollingoption-success.json): <b>UNVERIFIED</b> until the owner's probe result is recorded in ADR 0177.</summary>
internal static class BackfillSamples
{
    public static readonly TimeSpan Ist = TimeSpan.FromMinutes(330);

    public static readonly Guid NiftyId = Guid.Parse("00000000-0000-0000-0000-0000000000c1");

    public static readonly Guid BankNiftyId = Guid.Parse("00000000-0000-0000-0000-0000000000c2");

    public static readonly Guid StockId = Guid.Parse("00000000-0000-0000-0000-0000000000c3");

    public static readonly BackfillTarget Nifty = new(NiftyId, "NSE:NIFTY", 13, true, 5_000);

    public static readonly BackfillTarget BankNifty = new(BankNiftyId, "NSE:BANKNIFTY", 25, true, 10_000);

    public static readonly BackfillTarget Stock = new(StockId, "NSE:RELIANCE", 2885, false, 50);

    public static DateTimeOffset At(int day, int hour, int minute, int second = 0) => new(2026, 10, day, hour, minute, second, Ist);

    /// <summary>The Unix seconds of an India time on 2026-10-12.</summary>
    public static long Epoch(int hour, int minute, int day = 12) => At(day, hour, minute).ToUnixTimeSeconds();

    /// <summary>Three bars (09:15, 09:20, 09:25 India time) on the second day of the request's window, so that every window has bars of its own.</summary>
    public static long[] TimesIn(RollingRequest request)
    {
        var day = new DateTimeOffset(request.From.AddDays(2).ToDateTime(TimeOnly.MinValue), Ist);
        return [day.AddHours(9).AddMinutes(15).ToUnixTimeSeconds(), day.AddHours(9).AddMinutes(20).ToUnixTimeSeconds(), day.AddHours(9).AddMinutes(25).ToUnixTimeSeconds()];
    }

    public static BackfillItem Item(string side = "CE", BackfillTarget? target = null, int? step = 5_000) =>
        new(new BackfillKey((target ?? Nifty).InstrumentId, 'W', 1, 10, side, 5, new DateOnly(2026, 9, 1)), (target ?? Nifty).Scrip, "OPTIDX", step);

    /// <summary>An answer with three bars at 09:15, 09:20 and 09:25 India time on the given side; the other side is null.</summary>
    public static byte[] Body(string side = "CE", params long[] times)
    {
        times = times.Length == 0 ? [Epoch(9, 15), Epoch(9, 20), Epoch(9, 25)] : times;
        string Numbers(Func<int, string> value) => "[" + string.Join(',', times.Select((_, i) => value(i))) + "]";
        var own = string.Join(',',
            "\"iv\":" + Numbers(i => (12.31 + i).ToString(CultureInfo.InvariantCulture)),
            "\"oi\":" + Numbers(i => (1523000 + i).ToString(CultureInfo.InvariantCulture)),
            "\"strike\":" + Numbers(_ => "25500"),
            "\"spot\":" + Numbers(i => (25012.3 + i).ToString(CultureInfo.InvariantCulture)),
            "\"open\":" + Numbers(i => (48.2 + i).ToString(CultureInfo.InvariantCulture)),
            "\"high\":" + Numbers(i => (52.3 + i).ToString(CultureInfo.InvariantCulture)),
            "\"low\":" + Numbers(i => (47.9 + i).ToString(CultureInfo.InvariantCulture)),
            "\"close\":" + Numbers(i => (51.05 + i).ToString(CultureInfo.InvariantCulture)),
            "\"volume\":" + Numbers(i => (128450 + i).ToString(CultureInfo.InvariantCulture)),
            "\"timestamp\":[" + string.Join(',', times.Select(t => t.ToString(CultureInfo.InvariantCulture))) + "]");
        var ce = side == "CE" ? "{" + own + "}" : "null";
        var pe = side == "PE" ? "{" + own + "}" : "null";
        return Encoding.UTF8.GetBytes($"{{\"data\":{{\"ce\":{ce},\"pe\":{pe}}}}}");
    }

    public static byte[] Text(string json) => Encoding.UTF8.GetBytes(json);

    /// <summary>Settings that plan only NIFTY: one year, offset 0, no shares: 3 series x 2 sides x 13 windows = 78 keys.</summary>
    public static OptionBackfillSettings Small { get; } = OptionBackfillSettings.Off with { Enabled = true, Years = 1, IndexOffsets = 0, StockCount = 0 };

    public static int Keys(OptionBackfillSettings settings, params BackfillTarget[] targets) => BackfillPlanner.Plan(targets, settings, new DateOnly(2026, 10, 12)).Count();
}
