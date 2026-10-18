namespace XDrishti.Simulation.Scanning;

/// <summary>The codes of the errors the entry scanner returns.</summary>
internal static class ScannerCodes
{
    /// <summary>The stop is of a kind, buffer or timeframe the scanner does not read yet.</summary>
    public const string StopUnsupported = "simulation.scanner_stop_unsupported";

    /// <summary>The evaluation timeframe is 1D: a daily bar is always stale by the rule of D1.</summary>
    public const string TimeframeUnsupported = "simulation.scanner_timeframe_unsupported";
}
