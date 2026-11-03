namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary>The one port to NSE's end-of-day archive: GET of one file for one date. Never called while <see cref="IEodSettings.Enabled"/> is off (the handlers check first).</summary>
public interface IEodSource
{
    /// <summary>Fetches the file of <paramref name="kind"/> for the India date.</summary>
    ValueTask<EodFetch> FetchAsync(EodFileKind kind, DateOnly date, CancellationToken cancellationToken);
}
