namespace XDrishti.Application.OptionQuality;

/// <summary>What one run did: the sessions and results written, whether it stopped early at its bounds, and how many units failed (they stay pending for the next run).</summary>
public sealed record OptionQualityRunSummary(long RunId, int Sessions, int Results, bool Stopped, int Failures = 0)
{
    public string Text => $"{Sessions} sessions, {Results} results" + (Failures > 0 ? $", {Failures} units failed and were left for the next run" : string.Empty) + (Stopped ? ", stopped at its bounds; the rest waits for the next run" : string.Empty);
}
