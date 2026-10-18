using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Market.Calendar;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>
/// A hand-calculated swing case of P3-03d written as the spec writes it: <b>setup A</b> (BUY, trigger close 100.00, stop 99.00, <c>FIXED_RR 2.0</c>, decision 10:00 on the entry date, tick 0.05, the doc 16 slippage) with the fill minute
/// <c>10:00 100.00/100.30/99.80/100.20</c>, the rest of the entry day repeating 100.20, and then further days. A day after the first starts at the last close before it (a minute not listed repeats the previous close), has Volume 1,000
/// per minute and is dense from 09:15 to 15:29 unless the case removes minutes, ends it early or leaves the date out.
/// </summary>
internal sealed class SwingHand
{
    public const string FillMinute = "10:00 100.00/100.30/99.80/100.20";

    private readonly List<Bar> _bars = [];

    private readonly DateOnly _entryDate;

    private SwingHand(string name, DateOnly entryDate, double stop, double? rewardRisk, decimal tick)
    {
        Name = name;
        _entryDate = entryDate;
        Stop = stop;
        RewardRisk = rewardRisk;
        Tick = tick;
    }

    public string Name { get; }

    public double Stop { get; }

    public double? RewardRisk { get; }

    public decimal Tick { get; }

    public int Sessions { get; private set; } = 5;

    public TradingCalendar? Calendar { get; private set; }

    /// <summary>Setup A on <paramref name="entryDate"/>: the entry day with its fill minute and <paramref name="extraRows"/> (rows of the entry day after the fill minute).</summary>
    public static SwingHand A(string name, int sessions, TradingCalendar? calendar = null, DateOnly? entryDate = null, params string[] extraRows)
    {
        var date = entryDate ?? TestCalendars.Mon;
        var hand = new SwingHand(name, date, 99.00, 2.0, 0.05m) { Sessions = sessions, Calendar = calendar };
        hand._bars.AddRange(Day.Dense(date).Start(100.00).Set(FillMinute).Set(extraRows).Bars());
        return hand;
    }

    /// <summary>The same case on another list of days: a later day that starts at the last close, with the listed rows.</summary>
    public SwingHand Then(DateOnly date, params string[] rows) => ThenWith(date, null, null, rows);

    /// <summary>A later day with rows; <paramref name="endsAfter"/> removes every minute after it; <paramref name="emptyRows"/> are minutes with volume 0.</summary>
    public SwingHand ThenWith(DateOnly date, string? endsAfter, string[]? emptyRows, params string[] rows)
    {
        var day = Day.Dense(date).Start(_bars[^1].Close).Set(rows);
        foreach (var empty in emptyRows ?? [])
        {
            day.SetEmpty(empty);
        }

        if (endsAfter is not null)
        {
            day.EndsAfter(endsAfter);
        }

        _bars.AddRange(day.Bars());
        return this;
    }

    /// <summary>Removes every minute of the list after <paramref name="clock"/> on <paramref name="date"/> (and every later date).</summary>
    public SwingHand EndsAfter(DateOnly date, string clock)
    {
        var cut = Day.Epoch(date, Day.Minute(clock));
        _bars.RemoveAll(b => b.EpochSeconds > cut);
        return this;
    }

    /// <summary>A whole date with its minutes: flat at the last close.</summary>
    public SwingHand Flat(DateOnly date) => Then(date);

    public IReadOnlyList<Bar> Bars => _bars;

    public EntryCandidate Candidate()
    {
        var ownEpoch = Day.Epoch(_entryDate, Day.Minute("10:00") - 1);
        var index = _bars.FindIndex(b => b.EpochSeconds == ownEpoch);
        index.ShouldBeGreaterThanOrEqualTo(0, Name + ": the candidate's own minute must be in the list");
        return Cand.Make(Side.Buy, index, "10:00", Stop, "09:45", (100.00, 100.00, 100.00, 100.00), date: _entryDate);
    }

    public WalkRules Rules(FillRules fills) => Rules(fills, Sessions, Calendar);

    public WalkRules Rules(FillRules fills, int sessions, TradingCalendar? calendar) =>
        new(RewardRisk, WalkRules.NoSquareOff, fills, TickSpec.FromRupees(Tick).Value, SwingHold.Create(sessions, calendar).Value);

    public WalkOutcome Walk(FillRules? fills = null)
    {
        var result = TradeWalker.Walk(Candidate(), _bars, Rules(fills ?? FillRules.Default));
        result.IsSuccess.ShouldBeTrue(Name + ": " + (result.IsFailure ? result.Error.Code : string.Empty));
        return result.Value;
    }

    public override string ToString() => Name;
}
