
namespace XDrishti.Simulation.Tests.Oracle;

/// <summary>The numbers copied from a spec table (or computed by hand by O for a supplementary row).</summary>
internal sealed class Expect
{
    public bool Trade { get; init; } = true;

    public OracleNoTradeReason? NoTrade { get; init; }

    public decimal? Entry { get; init; }

    public decimal? Exit { get; init; }

    public OracleExitReason? Reason { get; init; }

    public decimal? R { get; init; }

    public decimal? Mae { get; init; }

    public decimal? Mfe { get; init; }

    public OracleEntryFill Fill { get; init; } = OracleEntryFill.AtOpen;

    public decimal? Target { get; init; }

    public bool TargetIsNull { get; init; }

    public decimal? EntryF { get; init; }

    public decimal? ExitF { get; init; }

    public decimal? RF { get; init; }

    public decimal? SlipR { get; init; }

    public string? EntryAt { get; init; }

    public string? ExitAt { get; init; }

    public string? TradeDate { get; init; }

    public int Held { get; init; } = 1;

    public int Unseen { get; init; }

    public int? Bars { get; init; }

    public Dictionary<string, int> Extra { get; init; } = [];

    public bool CheckCounters { get; init; } = true;
}

/// <summary>One encoded hand row: minutes, candidate, rules (with the slippage of the row) and the expected numbers.</summary>
internal sealed class HandRow
{
    public required string Id { get; init; }

    public required string Source { get; init; }

    public required OracleBar[] Bars { get; init; }

    public required OracleCandidate Candidate { get; init; }

    public required OracleRules Rules { get; init; }

    public required Expect Expect { get; init; }

    public override string ToString() => Id;
}
