using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Simulation.Running;

/// <summary>
/// The stable codes of what <see cref="Simulator"/> refuses by name (spec P3-03a, criteria 11 and 12). A program switches on the code and the path; the messages are English and may change. The codes of the stages
/// (<c>simulation.rules_invalid</c>, <c>simulation.candidate_not_in_bars</c>, <c>simulation.scanner_stop_unsupported</c>) are returned unchanged when they come from those stages.
/// </summary>
public static class SimulatorCodes
{
    /// <summary>An entry kind that needs an order across bars (<c>stop_above_high</c>, <c>limit_pullback</c>). Path <c>buy.entry</c>.</summary>
    public const string UnsupportedEntry = "simulation.unsupported_entry";

    /// <summary>A stop kind that is not a level (<c>fixed_pct</c>, <c>atr_mult</c>), a level stop with a buffer, or one read on another timeframe. Path <c>buy.stop</c> (or a field of it).</summary>
    public const string UnsupportedStop = "simulation.unsupported_stop";

    /// <summary>Exits other than exactly one <c>FIXED_RR</c> with exactly one <c>rr</c> value, or any overlay. Path <c>buy.exits</c> or <c>buy.overlays[i]</c>.</summary>
    public const string UnsupportedExit = "simulation.unsupported_exit";

    /// <summary>A filter the strategy names (nothing evaluates them yet). Path <c>filters[i]</c>.</summary>
    public const string FilterNotAvailable = "simulation.filter_not_available";

    /// <summary>A side that reads the market context. Path <c>buy</c>.</summary>
    public const string NeedsContext = "simulation.needs_context";

    /// <summary>A side that reads the expiry dates. Path <c>buy</c>.</summary>
    public const string NeedsExpiryDays = "simulation.needs_expiry_days";

    /// <summary>The evaluation timeframe is 1W or 1M: only 1D is evaluated from the daily bar (ADR 0043). Path <c>timeframes</c>. The scanner returns the same code.</summary>
    public const string ScannerTimeframeUnsupported = ScannerCodes.TimeframeUnsupported;

    /// <summary>Two equal epochs or a descending pair among the minutes of a run. Returned by <c>Run</c> before any step.</summary>
    public const string BarsNotAscending = SimulationErrors.BarsNotAscendingCode;
}
