namespace XDrishti.SimOracle;

/// <summary>The 32 counters of one walk (or of several, summed by <see cref="Plus"/>).</summary>
public sealed record OracleCounters
{
    public int Candidates { get; init; }

    public int Trades { get; init; }

    public int NoFillBar { get; init; }

    public int AfterSquareOff { get; init; }

    public int EntryThroughStop { get; init; }

    public int ExitsStop { get; init; }

    public int ExitsTarget { get; init; }

    public int ExitsSquareOff { get; init; }

    public int ExitsEndOfData { get; init; }

    public int StopGapExits { get; init; }

    public int TargetGapExits { get; init; }

    public int EntriesAfterMissingMinutes { get; init; }

    public int BarsExamined { get; init; }

    public int LimitNotReached { get; init; }

    public int EntriesAtOpen { get; init; }

    public int EntriesAtLimit { get; init; }

    public int AmbiguousMinutes { get; init; }

    public int LateSquareOffs { get; init; }

    public int ZeroVolumeMinutes { get; init; }

    public int RiskUnderFourTicks { get; init; }

    public int PositionOpen { get; init; }

    public int ExitsMaxHold { get; init; }

    public int MaxHoldAtLastClose { get; init; }

    public int OvernightStopGaps { get; init; }

    public int OvernightTargetGaps { get; init; }

    public int SessionsHeld { get; init; }

    public int SessionsUnseen { get; init; }

    public int OvernightEntries { get; init; }

    public int OrderPendingAtEnd { get; init; }

    public int LegsTotal { get; init; }

    public int LegsMerged { get; init; }

    public int StopLoosenIgnored { get; init; }

    public int ExitsEmaExit { get; init; }
    public int ExitsTimeStop { get; init; }

    public int TimeStopAtLastClose { get; init; }

    /// <summary>The sum of two counter sets, property by property.</summary>
    public OracleCounters Plus(OracleCounters other) => new()
    {
        Candidates = Candidates + other.Candidates,
        Trades = Trades + other.Trades,
        NoFillBar = NoFillBar + other.NoFillBar,
        AfterSquareOff = AfterSquareOff + other.AfterSquareOff,
        EntryThroughStop = EntryThroughStop + other.EntryThroughStop,
        ExitsStop = ExitsStop + other.ExitsStop,
        ExitsTarget = ExitsTarget + other.ExitsTarget,
        ExitsSquareOff = ExitsSquareOff + other.ExitsSquareOff,
        ExitsEndOfData = ExitsEndOfData + other.ExitsEndOfData,
        StopGapExits = StopGapExits + other.StopGapExits,
        TargetGapExits = TargetGapExits + other.TargetGapExits,
        EntriesAfterMissingMinutes = EntriesAfterMissingMinutes + other.EntriesAfterMissingMinutes,
        BarsExamined = BarsExamined + other.BarsExamined,
        LimitNotReached = LimitNotReached + other.LimitNotReached,
        EntriesAtOpen = EntriesAtOpen + other.EntriesAtOpen,
        EntriesAtLimit = EntriesAtLimit + other.EntriesAtLimit,
        AmbiguousMinutes = AmbiguousMinutes + other.AmbiguousMinutes,
        LateSquareOffs = LateSquareOffs + other.LateSquareOffs,
        ZeroVolumeMinutes = ZeroVolumeMinutes + other.ZeroVolumeMinutes,
        RiskUnderFourTicks = RiskUnderFourTicks + other.RiskUnderFourTicks,
        PositionOpen = PositionOpen + other.PositionOpen,
        ExitsMaxHold = ExitsMaxHold + other.ExitsMaxHold,
        MaxHoldAtLastClose = MaxHoldAtLastClose + other.MaxHoldAtLastClose,
        OvernightStopGaps = OvernightStopGaps + other.OvernightStopGaps,
        OvernightTargetGaps = OvernightTargetGaps + other.OvernightTargetGaps,
        SessionsHeld = SessionsHeld + other.SessionsHeld,
        SessionsUnseen = SessionsUnseen + other.SessionsUnseen,
        OvernightEntries = OvernightEntries + other.OvernightEntries,
        OrderPendingAtEnd = OrderPendingAtEnd + other.OrderPendingAtEnd,
        LegsTotal = LegsTotal + other.LegsTotal,
        LegsMerged = LegsMerged + other.LegsMerged,
        StopLoosenIgnored = StopLoosenIgnored + other.StopLoosenIgnored,
        ExitsEmaExit = ExitsEmaExit + other.ExitsEmaExit,
        ExitsTimeStop = ExitsTimeStop + other.ExitsTimeStop,
        TimeStopAtLastClose = TimeStopAtLastClose + other.TimeStopAtLastClose,
    };
}
