namespace XDrishti.Simulation.Scanning;

/// <summary>The codes of the errors the entry scanner returns.</summary>
internal static class ScannerCodes
{
    /// <summary>The stop is of a kind, buffer or timeframe the scanner does not read yet.</summary>
    public const string StopUnsupported = "simulation.scanner_stop_unsupported";

    /// <summary>The evaluation timeframe is 1W or 1M, which the scanner does not evaluate (1D is evaluated at the step of the 15:29 minute).</summary>
    public const string TimeframeUnsupported = "simulation.scanner_timeframe_unsupported";
}
