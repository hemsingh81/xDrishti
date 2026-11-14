using XDrishti.Domain.Common;

namespace XDrishti.Application.Replay.Sim;

/// <summary>The phases and the error codes of the day-by-day simulation (SR-04).</summary>
public static class SimDays
{
    public const string Idle = "idle";
    public const string Settling = "settling";
    public const string Settled = "settled";
    public const string Complete = "complete";
    public const string Cancelled = "cancelled";
    public const string Failed = "error";

    public const string InvalidCode = "sim.invalid";
    public const string BusyCode = "sim.busy";
    public const string NotSettledCode = "sim.not_settled";
    public const string NotFoundCode = "sim.not_found";
    public const string FinalCode = "sim.final";
    public const string NotCompleteCode = "sim.not_complete";
    public const string NothingNewCode = "sim.nothing_new";

    /// <summary>Trading sessions after the first stored bar before a simulation may start: the cell statistics and the indicators need a history to read.</summary>
    public const int WarmUpSessions = 20;

    /// <summary>Whether a run in <paramref name="phase"/> is active (at most one is).</summary>
    public static bool IsActive(string phase) => phase is Idle or Settling or Settled;

    internal static Error Invalid(string message) => Error.Validation(InvalidCode, message);

    internal static Error NotFound => Error.NotFound(NotFoundCode, "No such simulation.");

    internal static Error Busy(string message) => Error.Conflict(BusyCode, message);
}
