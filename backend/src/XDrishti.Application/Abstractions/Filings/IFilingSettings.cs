namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The settings <c>Filings:LagMinutes|MaxFilesGb|MinFreeGb</c> (P15-02). A deployment setting, never owner-switchable: the lag changes research results.</summary>
public interface IFilingSettings
{
    /// <summary>Minutes between the exchange broadcast and the moment a filing counts as known.</summary>
    int LagMinutes { get; }

    long MaxFilesBytes { get; }

    long MinFreeBytes { get; }
}
