namespace XDrishti.Domain.Market;

/// <summary>Progress of a running refresh, in order.</summary>
public enum RefreshPhase
{
    Downloading,
    Parsing,
    Enriching,
    Saving,
}
