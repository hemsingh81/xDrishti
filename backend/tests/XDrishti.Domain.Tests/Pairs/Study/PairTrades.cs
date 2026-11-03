using XDrishti.Domain.Lab;
using XDrishti.Domain.Market;
using XDrishti.Domain.PairResearch;

namespace XDrishti.Domain.Tests.Pairs.Study;

/// <summary>Seeded stored pair trades for the protocol S tests: <c>perQuarter</c> trades on consecutive days of each calendar quarter from 2023Q1, R drawn from a normal with the given mean and sd, spread over ten pairs, net rupees equal to R times a risk of 600 and gross 30 rupees above.</summary>
internal static class PairTrades
{
    internal static readonly PairStudyOptions Fast = PairStudyOptions.Default with { Draws = 1000 };

    internal static List<PairTradeSample> Make(ulong seed, double mean, double sd = 1.0, int quarters = 9, int perQuarter = 60, int pairs = 10, bool net = true, Func<string, double>? pairShift = null)
    {
        var rng = new SeededRandom(seed);
        var list = new List<PairTradeSample>();
        for (var q = 0; q < quarters; q++)
        {
            var start = new DateOnly(2023, 1, 1).AddMonths(3 * q);
            for (var i = 0; i < perQuarter; i++)
            {
                var pair = "NSE:Y" + (i % pairs).ToString(System.Globalization.CultureInfo.InvariantCulture) + "|NSE:X";
                var r = mean + (sd * PairsData.Normal(rng)) + (pairShift?.Invoke(pair) ?? 0.0);
                var date = start.AddDays(i);
                var epoch = new DateTimeOffset(date.ToDateTime(new TimeOnly(10, 0)), IndiaTime.Offset).ToUnixTimeSeconds();
                var netInr = (decimal)Math.Round(r * 600.0, 2);
                list.Add(new PairTradeSample(date, epoch, pair, r, netInr + 30m, net ? netInr : null, 200_000m));
            }
        }

        return list;
    }

    internal static ProtocolSInput Input(List<PairTradeSample> trades, int trials = 2006, PairStudyOptions? options = null) => new(trades, trials, [], [], options ?? Fast);
}
