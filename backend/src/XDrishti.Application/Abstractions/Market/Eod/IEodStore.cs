namespace XDrishti.Application.Abstractions.Market.Eod;

/// <summary><c>market.daily_eod</c> and its file ledger <c>market.eod_files</c>. xd_app has no DELETE on either; a day is written again by upsert, so a re-run changes nothing.</summary>
public interface IEodStore
{
    /// <summary>The dates whose bhavcopy is already in the ledger (loaded, or recorded as absent at NSE). With <paramref name="recheckAbsent"/> the days recorded as absent are not counted as done, so they are asked for again.</summary>
    ValueTask<IReadOnlySet<DateOnly>> ListDoneDatesAsync(DateOnly from, DateOnly to, bool recheckAbsent, CancellationToken cancellationToken);

    /// <summary>The dates in the range that <c>market.trading_days</c> confirms as special sessions (a Muhurat Sunday, a Saturday session): days to load although they are weekends, and days that must have a file.</summary>
    ValueTask<IReadOnlySet<DateOnly>> ListSpecialSessionDatesAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);

    /// <summary>Writes one day (rows and ledger entries) in one transaction. <paramref name="deliverySha"/> is null when there was no delivery file.</summary>
    ValueTask WriteDayAsync(DateOnly date, IReadOnlyList<EodRow> rows, string bhavSha, string? deliverySha, CancellationToken cancellationToken);

    /// <summary>Records that NSE has no bhavcopy for the date (a holiday), so a re-run does not ask again.</summary>
    ValueTask MarkAbsentAsync(DateOnly date, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<EodUniverseRow>> UniverseAsync(DateOnly asOf, CancellationToken cancellationToken);

    /// <summary>Daily rows with minute volume and high of the same session. Sessions before an applied split or bonus whose ex-date is on or before the instrument's csvAdjustedThrough are left out: the CSV minute bars are adjusted at source, the daily rows are raw.</summary>
    ValueTask<IReadOnlyList<DailyMinuteRow>> DailyAgainstMinuteAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
