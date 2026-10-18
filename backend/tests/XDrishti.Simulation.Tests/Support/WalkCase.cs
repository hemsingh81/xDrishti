using XDrishti.Domain.Market.Bars;
using XDrishti.Domain.Research;
using XDrishti.Simulation.Walking;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>What the spec says one trade is, worked out by hand: times are <c>HH:MM</c> of the fixture date.</summary>
internal sealed record ExpectedTrade(
    string EntryTime,
    double Entry,
    double Stop,
    double? Target,
    string ExitTime,
    double Exit,
    ExitReason Reason,
    double R,
    double Mae,
    double Mfe,
    int Minutes,
    bool Delayed = false,
    bool StopGap = false,
    bool TargetGap = false,
    bool Ambiguous = false,
    bool Late = false);

/// <summary>One hand-made candidate of a fixture and what the spec says it gives: a trade or the reason there is none.</summary>
internal sealed record WalkItem(EntryCandidate Candidate, ExpectedTrade? Trade, NoTradeReason? NoTrade, int Minutes)
{
    public static WalkItem Trades(EntryCandidate candidate, ExpectedTrade trade) => new(candidate, trade, null, trade.Minutes);

    public static WalkItem None(EntryCandidate candidate, NoTradeReason reason, int minutes) => new(candidate, null, reason, minutes);
}

/// <summary>A fixture day or a row of the walker table: the minutes, the exit rules and the candidates walked over them.</summary>
internal sealed record WalkCase(string Name, IReadOnlyList<Bar> Bars, WalkRules Rules, IReadOnlyList<WalkItem> Items)
{
    public override string ToString() => Name;
}
