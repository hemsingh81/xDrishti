using XDrishti.Application.Market.Quality;
using XDrishti.Domain.Market.Quality;

namespace XDrishti.Application.Abstractions.Market.Quality;

/// <summary>An instrument with its latest stored result (null parts when it was never checked), for the detail view.</summary>
public sealed record StoredQualityResult(
    Guid InstrumentId,
    string Symbol,
    string Name,
    string Type,
    bool Tracked,
    string? Source,
    DateTimeOffset? FirstBar,
    DateTimeOffset? LastBar,
    Guid? RunId,
    DateTimeOffset? CheckedAt,
    string? Status,
    int? Score,
    int? BarsChecked,
    int? MissingMinutes,
    QualityChecks? Checks);

/// <summary>Reads of the stored quality results (all reads, no tracking). The current result of an instrument is the one of the latest run that checked it.</summary>
public interface IQualityQueries
{
    /// <summary>The counts of the current results over the tracked instruments with bars, and their mean score.</summary>
    ValueTask<QualityCounts> GetCountsAsync(CancellationToken cancellationToken);

    /// <summary>The latest run: of any kind, or only the ones over every instrument (<paramref name="allInstruments"/>); null when there is none.</summary>
    ValueTask<QualityRunView?> GetLastRunAsync(bool allInstruments, CancellationToken cancellationToken);

    /// <summary>The latest run over every instrument that has finished (succeeded or failed); a run still running is skipped. Null when there is none. This is what the gate judges.</summary>
    ValueTask<QualityRunView?> GetLastFinishedFullRunAsync(CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<QualityRunView>> ListRunsAsync(int take, CancellationToken cancellationToken);

    ValueTask<QualityInstrumentPage> ListInstrumentsAsync(QualityInstrumentSearch search, CancellationToken cancellationToken);

    /// <summary>The instrument with its latest result, details included; null when the instrument does not exist.</summary>
    ValueTask<StoredQualityResult?> FindAsync(Guid instrumentId, CancellationToken cancellationToken);
}
