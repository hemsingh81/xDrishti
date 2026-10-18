using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// One hand-calculated case of P3-03b written as the spec writes it: a ticket (side, trigger close, stop, tick, reward-to-risk, square-off, decision instant) and the minutes after it. A listed minute is
/// <c>"HH:MM O/H/L/C"</c> with volume 1,000, <c>"... v0"</c> with volume 0; a minute not listed repeats the previous close; the candidate's own minute starts 60 s before the decision instant and repeats the trigger close.
/// The date is 2026-03-02.
/// </summary>
internal sealed class Hand
{
    private readonly List<string> _rows = [];

    private readonly List<(string From, string To)> _absent = [];

    private string? _endsAfter;

    private Hand(string name, Side side, double triggerClose, double stop)
    {
        Name = name;
        Side = side;
        TriggerClose = triggerClose;
        Stop = stop;
    }

    public string Name { get; }

    /// <summary>The listed minutes, as written (the shifted copies of the overnight invariance are built from them).</summary>
    public IReadOnlyList<string> ListedRows => _rows;

    /// <summary>The absent ranges, as written.</summary>
    public IReadOnlyList<(string From, string To)> AbsentRanges => _absent;

    public Side Side { get; }

    public double TriggerClose { get; }

    public double Stop { get; }

    public decimal Tick { get; private set; } = 0.05m;

    public double? RewardRisk { get; private set; } = 2.0;

    public int SquareOffMinute { get; private set; } = 915;

    public string Decision { get; private set; } = "10:00";

    public static Hand Buy(string name, double triggerClose = 100.00, double stop = 99.00) => new(name, Side.Buy, triggerClose, stop);

    public static Hand Sell(string name, double triggerClose, double stop) => new(name, Side.Sell, triggerClose, stop);

    public Hand WithTick(decimal tick)
    {
        Tick = tick;
        return this;
    }

    public Hand WithRewardRisk(double? rewardRisk)
    {
        RewardRisk = rewardRisk;
        return this;
    }

    public Hand DecidesAt(string decision)
    {
        Decision = decision;
        return this;
    }

    /// <summary>Lists minutes; a row that ends with <c>v0</c> has volume 0.</summary>
    public Hand Rows(params string[] rows)
    {
        _rows.AddRange(rows);
        return this;
    }

    public Hand Absent(string from, string to)
    {
        _absent.Add((from, to));
        return this;
    }

    public Hand EndsAfter(string last)
    {
        _endsAfter = last;
        return this;
    }

    public Bar[] Bars()
    {
        var day = Day.Dense().Start(TriggerClose);
        foreach (var row in _rows)
        {
            if (row.EndsWith(" v0", StringComparison.Ordinal))
            {
                day.SetEmpty(row[..^3]);
            }
            else
            {
                day.Set(row);
            }
        }

        foreach (var (from, to) in _absent)
        {
            day.Absent(from, to);
        }

        if (_endsAfter is not null)
        {
            day.EndsAfter(_endsAfter);
        }

        return day.Bars();
    }

    public EntryCandidate Candidate(IReadOnlyList<Bar> bars)
    {
        var ownEpoch = Day.Epoch(Day.Monday, Day.Minute(Decision) - 1);
        var index = bars.ToList().FindIndex(b => b.EpochSeconds == ownEpoch);
        index.ShouldBeGreaterThanOrEqualTo(0, Name + ": the candidate's own minute must be in the list");
        return Cand.Make(Side, index, Decision, Stop, "09:45", (TriggerClose, TriggerClose, TriggerClose, TriggerClose));
    }

    public WalkRules Rules(FillRules fills) => new(RewardRisk, SquareOffMinute, fills, TickSpec.FromRupees(Tick).Value);

    public WalkOutcome Walk(FillRules fills)
    {
        var bars = Bars();
        var result = TradeWalker.Walk(Candidate(bars), bars, Rules(fills));
        result.IsSuccess.ShouldBeTrue(Name + ": " + (result.IsFailure ? result.Error.Code : string.Empty));
        return result.Value;
    }

    public override string ToString() => Name;
}
