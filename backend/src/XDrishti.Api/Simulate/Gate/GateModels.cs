using XDrishti.SimReader;

namespace XDrishti.Api.Simulate.Gate;

/// <summary>The decision of the automated gate (spec P3-09d, ADR 0048). The order is the precedence: a later value never masks an earlier one.</summary>
internal enum GateStatus
{
    Pass,
    PassWithDataFindings,
    RerunNeeded,
    DataRepairNeeded,
    BlockedDefect,
}

/// <summary>The spellings and the exit codes of the statuses. 0 for both passing statuses (the exit of P3 is one of them), 10 to 12 for the three that need work.</summary>
internal static class GateStatuses
{
    public static string Code(GateStatus status) => status switch
    {
        GateStatus.Pass => "PASS",
        GateStatus.PassWithDataFindings => "PASS_WITH_DATA_FINDINGS",
        GateStatus.RerunNeeded => "RERUN_NEEDED",
        GateStatus.DataRepairNeeded => "DATA_REPAIR_NEEDED",
        GateStatus.BlockedDefect => "BLOCKED_DEFECT",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };

    public static int ExitCode(GateStatus status) => status switch
    {
        GateStatus.Pass or GateStatus.PassWithDataFindings => 0,
        GateStatus.RerunNeeded => 10,
        GateStatus.DataRepairNeeded => 11,
        GateStatus.BlockedDefect => 12,
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>One thing the gate found, on one row of the pack (Seq 0: not a row). <see cref="Detail"/> is the repro or the list the next action needs.</summary>
internal sealed record GateFinding(int Seq, string TradeKey, string Symbol, DateOnly Date, string Detail);

/// <summary>A spot-check line of <c>pack.json</c>: its key <c>spot|kind[:detail]|symbol|date</c> split into the parts.</summary>
internal sealed record SpotLine(int Seq, string Key, string Kind, string Detail, string Symbol, DateOnly Date);

internal enum SpotOutcome
{
    Confirmed,
    Contradicted,
    NotMachineCheckable,
}

/// <summary>What the machine did with one spot line: exactly what it checked, and what it did not (and so what stays with the owner's optional chart check).</summary>
internal sealed record SpotResult(SpotLine Line, SpotOutcome Outcome, string Checked, string NotChecked);

/// <summary>The stored bars of a spot day, and the last bar of the nearest earlier day that has bars in the regular window (null: none within the look-back).</summary>
internal sealed record SpotDay(IReadOnlyList<ReaderBar> Bars, ReaderBar? PreviousLast);

/// <summary>Every count is of rows or lines, independent of the others: a row with a defect and a gross gap is counted in both.</summary>
internal sealed record GateCounts(int Rows, int Defects, int Repairs, int Reruns, int DataFindings, int SpotsConfirmed, int SpotsContradicted, int SpotsNotCheckable);

internal sealed record GateDecision(
    GateStatus Status,
    GateCounts Counts,
    IReadOnlyList<GateFinding> Defects,
    IReadOnlyList<GateFinding> Repairs,
    IReadOnlyList<GateFinding> Reruns,
    IReadOnlyList<GateFinding> DataFindings,
    IReadOnlyList<SpotResult> Spots,
    IReadOnlyList<string> NextActions);
