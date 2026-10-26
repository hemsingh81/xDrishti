using XDrishti.Domain.Reporting;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Reporting;

/// <summary>The wire spellings of the replay's enums (snake case, fixed).</summary>
internal static class ReplayNames
{
    public static string Name(ReplayCoherenceState state) => state switch
    {
        ReplayCoherenceState.Ok => "ok",
        ReplayCoherenceState.EntryMismatch => "entry_mismatch",
        _ => "no_bars",
    };

    public static string Name(LegKind kind) => kind switch
    {
        LegKind.Entry => "entry",
        LegKind.Add => "add",
        _ => "exit",
    };

    public static string Name(EntryFill fill) => fill == EntryFill.AtLimit ? "at_limit" : "at_open";

    public static string Name(ExitReason reason) => reason switch
    {
        ExitReason.Stop => "stop",
        ExitReason.Target => "target",
        ExitReason.SquareOff => "square_off",
        ExitReason.EndOfData => "end_of_data",
        ExitReason.MaxHold => "max_hold",
        ExitReason.EmaExit => "ema_exit",
        _ => "time_stop",
    };
}
