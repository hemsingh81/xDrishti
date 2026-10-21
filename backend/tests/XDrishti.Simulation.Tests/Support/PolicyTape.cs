using XDrishti.Domain.Market.Bars;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The rule families of P4-02 that the comparison, the properties and the benchmarks run.</summary>
public enum PolicyFamily
{
    /// <summary><c>PARTIAL_BE</c>, with the time stop on every third seed.</summary>
    Be,

    /// <summary><c>PARTIAL_TRAIL</c>, with the time stop on every third seed.</summary>
    Trail,

    /// <summary><c>FIXED_RR</c> with the time stop.</summary>
    TimeStop,
}

/// <summary>One choice of the parameters of a family, derived from a seed so that every shape of D12 occurs: three books, three T1 levels, a second target, three trail multiples, the time stop in bars or in sessions.</summary>
internal sealed record PolicyVariant(PartialKind Kind, decimal Book, decimal T1Rr, decimal T2Rr, decimal Mult, decimal? MinProgress, int Bars, int Sessions)
{
    private static readonly decimal[] Books = [0.5m, 0.25m, 0.4m];

    private static readonly decimal[] T1s = [0.5m, 1.0m, 1.5m];

    private static readonly decimal[] T2Extra = [0.5m, 1.0m, 2.0m];

    private static readonly decimal[] Mults = [1.0m, 2.0m, 0.5m];

    private static readonly decimal[] Minimums = [0m, 0.5m, 1.0m];

    private static readonly int[] BarCounts = [1, 2, 3, 4, 6];

    /// <summary>The variant of a family for a series, a walk with a hold of <paramref name="holdSessions"/> sessions (0 for an intraday walk).</summary>
    internal static PolicyVariant Of(PolicyFamily family, long seed, int holdSessions)
    {
        var k = (int)(Math.Abs(seed) % 1_000_003);
        var kind = family switch
        {
            PolicyFamily.Be => PartialKind.Be,
            PolicyFamily.Trail => PartialKind.Trail,
            _ => PartialKind.None,
        };
        var withStop = family == PolicyFamily.TimeStop || k % 3 == 0;
        var t1 = T1s[(k / 3) % T1s.Length];
        decimal? minimum = null;
        var bars = 0;
        var sessions = 0;
        if (withStop)
        {
            if (holdSessions == 0)
            {
                minimum = Minimums[(k / 5) % Minimums.Length];
                bars = BarCounts[(k / 7) % BarCounts.Length];
            }
            else if (holdSessions >= 2)
            {
                minimum = Minimums[(k / 5) % Minimums.Length];
                sessions = 1 + ((k / 7) % (holdSessions - 1));
            }
        }

        return new PolicyVariant(kind, Books[k % Books.Length], t1, t1 + T2Extra[(k / 11) % T2Extra.Length], Mults[(k / 13) % Mults.Length], minimum, bars, sessions);
    }

    /// <summary>The engine's policy, or null when the variant asks for nothing (a time stop on a walk that cannot have one).</summary>
    internal LegPolicy? Engine() =>
        Kind == PartialKind.None && MinProgress is null ? null : new LegPolicy(Kind, Kind == PartialKind.None ? 0m : Book, Kind == PartialKind.Be ? T2Rr : 0m, Kind == PartialKind.Trail ? Mult : 0m, MinProgress, Bars, Sessions);

    /// <summary>The target of the position: T1 for a partial policy, else the reward-to-risk the generator gave.</summary>
    internal double? RewardRisk(double? generated) => Kind == PartialKind.None ? generated : (double)T1Rr;
}

/// <summary>
/// The closed 15-minute bars of a list of minutes for the P4-02 policies, in the two forms the tests need: the engine's <see cref="ExitTape"/> (the feature <c>atr</c> when the policy trails, else the closes only) and the oracle's
/// <see cref="OraclePolicyBar"/> list. The bars are those of <see cref="LadderTape"/> (the minutes of one date in one 15-minute bucket counted from 09:15; a bar closes at the end of its last minute present). The ATR of a bar is the mean
/// range of up to three bars ending at it, NaN for the first bar of the list and for every eleventh bar after it so that a value that is not ready occurs; both forms come from the same loop.
/// </summary>
internal static class PolicyTape
{
    private const int BucketMinutes = 15;

    private const int FirstMinute = 555;

    internal sealed record Built(ExitContext? Context, OraclePolicy Policy, ExitTape Tape);

    /// <summary>
    /// The tape and the policies for the minutes. <paramref name="leakEarlyBy"/> above 0 is the negative control of the leak property: each bar is made visible that many seconds after the start of its bucket instead of at its close, so
    /// that a rule sees the bar before it has closed (its values are those of the bar as the list ends, which is the bar as it is now, not as it will be).
    /// </summary>
    internal static Built Of(IReadOnlyList<Bar> minutes, PolicyVariant variant, int leakEarlyBy = 0)
    {
        var trails = variant.Kind == PartialKind.Trail;
        var tape = new ExitTape(trails ? ["atr"] : []);
        var bars = new List<OraclePolicyBar>();
        var ranges = new List<double>();
        var key = long.MinValue;
        double low = 0, high = 0, close = 0;
        var closeEpoch = 0L;
        var startEpoch = 0L;
        var open = false;
        var index = 0;
        foreach (var minute in minutes)
        {
            var date = SessionClock.DateOf(minute.EpochSeconds).DayNumber;
            var bucket = (date * 1000L) + (Math.Max(0, SessionClock.MinuteOfDay(minute.EpochSeconds) - FirstMinute) / BucketMinutes);
            if (open && bucket != key)
            {
                Close(tape, bars, ranges, ref index, leakEarlyBy > 0 ? startEpoch + leakEarlyBy : closeEpoch, close, low, high, trails);
                open = false;
            }

            if (!open)
            {
                key = bucket;
                low = minute.Low;
                high = minute.High;
                startEpoch = minute.EpochSeconds;
                open = true;
            }

            low = Math.Min(low, minute.Low);
            high = Math.Max(high, minute.High);
            close = minute.Close;
            closeEpoch = minute.EpochSeconds + 60;
        }

        if (open)
        {
            Close(tape, bars, ranges, ref index, leakEarlyBy > 0 ? startEpoch + leakEarlyBy : closeEpoch, close, low, high, trails);
        }

        var policy = new OraclePolicy(
            variant.Kind switch { PartialKind.Be => OraclePolicyKind.Be, PartialKind.Trail => OraclePolicyKind.Trail, _ => OraclePolicyKind.None },
            variant.Kind == PartialKind.None ? 0m : variant.Book,
            variant.Kind == PartialKind.Be ? variant.T2Rr : 0m,
            variant.Kind == PartialKind.Trail ? variant.Mult : 0m,
            variant.MinProgress,
            variant.Bars,
            variant.Sessions,
            bars);
        var engine = variant.Engine();
        return new Built(engine is null ? null : new ExitContext(null, tape, engine), policy, tape);
    }

    private static void Close(ExitTape tape, List<OraclePolicyBar> bars, List<double> ranges, ref int index, long closeEpoch, double close, double low, double high, bool trails)
    {
        ranges.Add(high - low);
        double atr;
        if (index == 0 || index % 11 == 10)
        {
            atr = double.NaN;
        }
        else
        {
            var window = ranges.Skip(Math.Max(0, ranges.Count - 3)).ToList();
            atr = window.Average();
        }

        index++;
        if (trails)
        {
            tape.Append(closeEpoch, close, [atr]);
        }
        else
        {
            tape.Append(closeEpoch, close, []);
        }

        bars.Add(new OraclePolicyBar(closeEpoch, close, atr));
    }
}
