namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>The resume point of the backfill (P14-03): what <c>options.fetch_log</c> says about each key. Reads the log only, never a bar.</summary>
public interface IBackfillFetchLog
{
    /// <summary>Every key that has an attempt, with its counts.</summary>
    ValueTask<IReadOnlyDictionary<BackfillKey, KeyState>> LoadStateAsync(CancellationToken cancellationToken);

    /// <summary>How many requests were sent since <paramref name="since"/> (attempts that reached Dhan: every row except a disk stop).</summary>
    ValueTask<int> CountSentSinceAsync(DateTimeOffset since, CancellationToken cancellationToken);
}
