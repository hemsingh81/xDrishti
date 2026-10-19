using System.Globalization;
using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.SimReader;
using XDrishti.Simulation;
using XDrishti.Simulation.Running;
using XDrishti.Strategies.BuiltIn;

namespace XDrishti.Api.IntegrationTests.Simulate;

/// <summary>
/// P3-09c criterion 3, the honest substitute: the oracle series of P3-08 are walks over given candidates, which a chart reader cannot read, so a seeded generator makes whole invented opening-range days
/// (buy and sell, gaps, stops in the entry minute, targets, square-offs, holes, zero-volume minutes, late days, empty ranges) and the real Simulator (scanner and walker) and the independent
/// chart reader read each one. Every field is compared (prices exact to 1e-6, times to the minute, reason, seen_r to 0.01). The two projects may meet only here: the reader references nothing.
/// </summary>
public sealed class ReaderVersusEngineTests
{
    private const int Seeds = 6000;

    private static readonly long Midnight = new DateTimeOffset(2026, 3, 2, 0, 0, 0, TimeSpan.FromHours(5.5)).ToUnixTimeSeconds();

    private static readonly decimal[] Ticks = [0.05m, 0.05m, 0.01m, 0.10m];

    /// <summary>One invented day: decimal minutes on the tick grid.</summary>
    internal sealed record Case(int Seed, decimal Tick, List<ReaderBar> Bars, string Features);

    internal static Case Make(int seed)
    {
        var rnd = new Random(seed);
        var tick = Ticks[rnd.Next(Ticks.Length)];
        decimal Snap(decimal v) => Math.Max(tick, Math.Round(v / tick, MidpointRounding.AwayFromZero) * tick);
        var price = Snap(50m + (decimal)rnd.NextDouble() * 500m);
        var vol = price * (0.0004m + (decimal)rnd.NextDouble() * 0.0016m);
        var drift = ((decimal)rnd.NextDouble() - 0.5m) * vol * 0.6m;
        var features = new List<string>();
        var gaps = rnd.Next(3) == 0 ? Enumerable.Range(0, rnd.Next(1, 4)).Select(_ => rnd.Next(2) == 0 ? rnd.Next(570, 700) : 555 + (15 * rnd.Next(1, 10))).ToHashSet() : [];
        var gapAt = gaps.Count > 0 ? gaps.Min() : -1;
        var zeroVolume = rnd.Next(3) == 0;
        var holeFrom = rnd.Next(4) == 0 ? rnd.Next(555, 900) : -1;
        var holeLength = rnd.Next(1, 12);
        var emptyRange = rnd.Next(60) == 0;
        var lastMinute = rnd.Next(12) == 0 ? rnd.Next(580, 930) : (15 * 60) + 29;
        var lateHole = rnd.Next(6) == 0;
        var pump = rnd.Next(100) < 35 ? 555 + (15 * rnd.Next(1, 9)) : -1;
        var spike = rnd.Next(100) < 30;
        var pumpUp = rnd.Next(2) == 0;
        var pumpGap = new[] { 0m, 0.0005m, 0.0015m, 0.004m }[rnd.Next(4)] * (rnd.Next(4) == 0 ? -1 : 1);
        decimal rangeHigh = decimal.MinValue, rangeLow = decimal.MaxValue;
        var bars = new List<ReaderBar>();
        for (var m = (9 * 60) + 15; m <= lastMinute; m++)
        {
            var inHole = (holeFrom >= 0 && m >= holeFrom && m < holeFrom + holeLength)
                || (lateHole && m >= (15 * 60) + 10 && m <= (15 * 60) + 17)
                || (emptyRange && m < (9 * 60) + 30);
            var open = price;
            if (gaps.Contains(m))
            {
                open = Snap(price + ((decimal)rnd.NextDouble() - 0.5m) * vol * 8m);
            }

            if (m == pump)
            {
                open = Snap(price * (1m + ((pumpUp ? 1m : -1m) * pumpGap)));
            }

            var close = Snap(open + drift + (((decimal)rnd.NextDouble() - 0.5m) * 2m * vol));
            var high = Snap(Math.Max(open, close) + (decimal)rnd.NextDouble() * vol * 0.6m);
            var low = Snap(Math.Min(open, close) - (decimal)rnd.NextDouble() * vol * 0.6m);
            low = Math.Min(low, Math.Min(open, close));
            high = Math.Max(high, Math.Max(open, close));
            if (m == pump && spike)
            {
                low = pumpUp ? Snap(rangeLow - tick) : low;
                high = pumpUp ? high : Snap(rangeHigh + tick);
            }

            if (m == pump - 1)
            {
                close = pumpUp ? Snap(rangeHigh + (2 * tick)) : Snap(rangeLow - (2 * tick));
                high = Math.Max(high, close);
                low = Math.Min(low, close);
            }

            if (m < (9 * 60) + 30)
            {
                rangeHigh = Math.Max(rangeHigh, high);
                rangeLow = Math.Min(rangeLow, low);
            }

            price = close;
            if (inHole)
            {
                continue;
            }

            var volume = zeroVolume && rnd.Next(8) == 0 ? 0m : 100m + rnd.Next(5000);
            bars.Add(new ReaderBar(Midnight + (m * 60L), open, high, low, close, volume));
        }

        features.Add(gapAt >= 0 ? "gap" : "nogap");
        features.Add(holeFrom >= 0 ? "hole" : "dense");
        features.Add(emptyRange ? "emptyrange" : "range");
        return new Case(seed, tick, bars, string.Join(' ', features));
    }

    private static Bar Engine(ReaderBar b) => new(b.EpochSeconds, (double)b.Open, (double)b.High, (double)b.Low, (double)b.Close, (long)b.Volume);

    private static string Hhmm(long epoch) => DateTimeOffset.FromUnixTimeSeconds(epoch).ToOffset(TimeSpan.FromMinutes(330)).ToString("HH:mm", CultureInfo.InvariantCulture);

    private static SimTrade? EngineTrade(Case c, Side side)
    {
        var definition = OrbV1.Create() with { Sides = [side], Sell = side == Side.Sell ? OrbV1.Create().Sell : null, Buy = side == Side.Buy ? OrbV1.Create().Buy : null };
        var build = Simulator.Create(definition, Timeframe.M15);
        build.Simulator.ShouldNotBeNull();
        var tick = TickSpec.FromRupees(c.Tick).Value;
        var result = build.Simulator.Run(new InstrumentRef(Guid.Empty, "TEST"), [.. c.Bars.Select(Engine)], tick);
        result.IsSuccess.ShouldBeTrue(c.Seed.ToString(CultureInfo.InvariantCulture));
        var first = result.Value.Outcomes.OrderBy(o => o.Candidate.DecisionEpochSeconds).FirstOrDefault();
        return first?.Walk.Trade;
    }

    private static ReaderExitReason Reason(ExitReason r) => r switch
    {
        ExitReason.Stop => ReaderExitReason.Stop,
        ExitReason.Target => ReaderExitReason.Target,
        ExitReason.SquareOff => ReaderExitReason.SquareOff,
        ExitReason.EndOfData => ReaderExitReason.EndOfData,
        _ => throw new InvalidOperationException(r.ToString()),
    };

    /// <summary>The fields on which the two disagree, empty when they agree.</summary>
    internal static List<string> Differences(ReaderReading? read, SimTrade? trade)
    {
        var diffs = new List<string>();
        if (read is null || trade is null)
        {
            if (read is not null || trade is not null)
            {
                diffs.Add(read is null ? "reader found no trade, engine has one" : "reader found a trade, engine has none");
            }

            return diffs;
        }

        void Price(string name, decimal a, double b)
        {
            if (Math.Abs((double)a - b) > 1e-6)
            {
                diffs.Add($"{name} reader {a} engine {b}");
            }
        }

        void Time(string name, long a, long b)
        {
            if (a != b)
            {
                diffs.Add($"{name} reader {Hhmm(a)} engine {Hhmm(b)}");
            }
        }

        Time("trigger_bar", read.TriggerBarStartEpoch, trade.TriggerBar.EpochSeconds);
        Price("trigger_close", read.TriggerClose, trade.TriggerBar.Close);
        Time("entry_time", read.EntryEpoch, trade.EntryEpochSeconds);
        Price("entry", read.EntryPrice, trade.EntryPrice);
        Price("stop", read.Stop, trade.StopPrice);
        if (trade.TargetPrice is { } engineTarget)
        {
            Price("target", read.Target, engineTarget);
        }
        else
        {
            diffs.Add($"target reader {read.Target} engine none");
        }

        Time("exit_time", read.ExitEpoch, trade.ExitEpochSeconds);
        Price("exit", read.ExitPrice, trade.ExitPrice);
        if (Reason(trade.ExitReason) != read.ExitReason)
        {
            diffs.Add($"reason reader {read.ExitReason} engine {trade.ExitReason}");
        }

        if (Math.Abs((double)read.SeenR - trade.RExact) > 0.01)
        {
            diffs.Add($"seen_r reader {read.SeenR} engine {trade.RExact}");
        }

        if (read.EntryAtLimit != (trade.EntryFill == EntryFill.AtLimit))
        {
            diffs.Add("entry_fill differs");
        }

        return diffs;
    }

    [Fact]
    public void The_generator_is_deterministic_and_reaches_every_case_of_the_protocol()
    {
        Make(7).Bars.ShouldBe(Make(7).Bars);

        var reads = new List<(ReaderSide Side, ReaderReading? Reading)>();
        var noTrade = 0;
        var emptyRange = 0;
        foreach (var seed in Enumerable.Range(1, 1500))
        {
            var c = Make(seed);
            emptyRange += c.Features.Contains("emptyrange", StringComparison.Ordinal) ? 1 : 0;
            foreach (var side in new[] { ReaderSide.Buy, ReaderSide.Sell })
            {
                var r = ChartReader.Read(c.Bars, ReaderStrategy.OrbV1(c.Tick), side);
                reads.Add((side, r));
                noTrade += r is null ? 1 : 0;
            }
        }

        foreach (var side in new[] { ReaderSide.Buy, ReaderSide.Sell })
        {
            foreach (var reason in Enum.GetValues<ReaderExitReason>())
            {
                reads.Count(x => x.Side == side && x.Reading?.ExitReason == reason).ShouldBeGreaterThanOrEqualTo(8, side + " " + reason);
            }
        }

        reads.Count(x => x.Reading is { EntryAtLimit: true }).ShouldBeGreaterThanOrEqualTo(25);
        reads.Count(x => x.Reading is { } r && r.ExitEpoch == r.EntryEpoch).ShouldBeGreaterThanOrEqualTo(10);
        noTrade.ShouldBeGreaterThanOrEqualTo(200);
        emptyRange.ShouldBeGreaterThanOrEqualTo(10);
    }

    [Fact]
    public void The_reader_and_the_engine_read_the_same_trade_on_every_invented_day()
    {
        var failures = new List<string>();
        var compared = 0;
        var trades = 0;
        for (var seed = 1; seed <= Seeds; seed++)
        {
            var c = Make(seed);
            foreach (var side in new[] { Side.Buy, Side.Sell })
            {
                var read = ChartReader.Read(c.Bars, ReaderStrategy.OrbV1(c.Tick), side == Side.Buy ? ReaderSide.Buy : ReaderSide.Sell);
                var diffs = Differences(read, EngineTrade(c, side));
                compared++;
                trades += read is null ? 0 : 1;
                if (diffs.Count > 0 && failures.Count < 20)
                {
                    failures.Add($"seed {seed} {side} [{c.Features}]: {string.Join("; ", diffs)}");
                }
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine($"{compared} comparisons, {trades} with a trade, {failures.Count} disagreements");
        compared.ShouldBe(Seeds * 2);
        trades.ShouldBeGreaterThan(Seeds / 4);
        failures.ShouldBeEmpty(string.Join('\n', failures));
    }

    [Fact]
    public void The_comparison_does_name_a_planted_difference()
    {
        var side = Enumerable.Range(1, 200).Select(Make).SelectMany(d => new[] { Side.Buy, Side.Sell }.Select(s => (d, s))).First(x => ChartReader.Read(x.d.Bars, ReaderStrategy.OrbV1(x.d.Tick), x.s == Side.Buy ? ReaderSide.Buy : ReaderSide.Sell) is not null);
        var read = ChartReader.Read(side.d.Bars, ReaderStrategy.OrbV1(side.d.Tick), side.s == Side.Buy ? ReaderSide.Buy : ReaderSide.Sell)!;
        var trade = EngineTrade(side.d, side.s)!;

        Differences(read, trade).ShouldBeEmpty();
        Differences(read with { ExitPrice = read.ExitPrice + 1m }, trade).ShouldContain(d => d.StartsWith("exit ", StringComparison.Ordinal));
        Differences(read with { Stop = read.Stop + 1m }, trade).ShouldContain(d => d.StartsWith("stop ", StringComparison.Ordinal));
        Differences(read with { ExitReason = read.ExitReason == ReaderExitReason.Stop ? ReaderExitReason.Target : ReaderExitReason.Stop }, trade).ShouldContain(d => d.StartsWith("reason ", StringComparison.Ordinal));
        Differences(read, trade with { TargetPrice = null }).ShouldContain(d => d.StartsWith("target ", StringComparison.Ordinal));
        Differences(null, trade).ShouldNotBeEmpty();
    }
}
