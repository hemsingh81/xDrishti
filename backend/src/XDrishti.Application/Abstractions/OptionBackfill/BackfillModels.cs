namespace XDrishti.Application.Abstractions.OptionBackfill;

/// <summary>An underlying the backfill asks about: its instrument, canonical symbol, Dhan id, whether it is an index and the smallest gap between two of its stored strikes in paise (null when unknown). Stocks come ranked by turnover, best first.</summary>
public sealed record BackfillTarget(Guid InstrumentId, string Symbol, long Scrip, bool IsIndex, int? StrikeStepPaise);

/// <summary>The identity of one fetch: a series (underlying, expiry kind and code, offset, side, interval) and a 30-day window starting at <see cref="From"/>. This is what the fetch log is keyed by.</summary>
public readonly record struct BackfillKey(Guid UnderlyingId, char ExpiryFlag, int ExpiryCode, int Offset, string Side, int IntervalMinutes, DateOnly From)
{
    /// <summary>Days in a window (Dhan: at most 30 a call).</summary>
    public const int WindowDays = 30;

    /// <summary>The day after the last day of the window (Dhan's <c>toDate</c> is not inclusive).</summary>
    public DateOnly To => From.AddDays(WindowDays);
}

/// <summary>A planned fetch: its key and what Dhan needs to ask for it.</summary>
public sealed record BackfillItem(BackfillKey Key, long SecurityId, string Instrument, int? StrikeStepPaise, DateOnly Earliest = default)
{
    /// <summary>The request; its first day is clamped to <see cref="Earliest"/> (Dhan goes back five years at most), the key keeps the grid's day.</summary>
    public RollingRequest ToRequest() => new(SecurityId, Instrument, Key.IntervalMinutes, Key.ExpiryFlag, Key.ExpiryCode, Key.Offset, Key.Side, Key.From < Earliest ? Earliest : Key.From, Key.To);
}

/// <summary>What the fetch log says about a key: attempts so far, failed attempts (a timeout, a server error or an unreadable body; a rate limit is not the key's fault), answers that could not be stored and the finishing outcome (<c>ok</c>, <c>empty</c> or <c>no_data</c>) if there is one.</summary>
public sealed record KeyState(int Attempts, int Failed, string? DoneOutcome, int StoreFailures = 0)
{
    /// <summary>Failed attempts after which a key is left <c>given_up</c> and listed.</summary>
    public const int MaxFailedAttempts = 3;

    /// <summary>Answers that could not be stored after which a key is given up (an answer that can never be stored would block the plan for ever).</summary>
    public const int MaxStoreFailures = 3;

    public bool IsDone => DoneOutcome is not null;

    public bool IsGivenUp => !IsDone && (Failed >= MaxFailedAttempts || StoreFailures >= MaxStoreFailures);
}

/// <summary>The flag bits of a stored bar (<c>options.rolling_bars.flags</c>).</summary>
public static class BackfillFlags
{
    /// <summary>The close was zero or negative, or the strike was not a multiple of the strike step: the bar is kept and labelled.</summary>
    public const short Suspect = 1;
}

/// <summary>One bar of a rolling option series, as stored. Prices in paise; a value Dhan did not send is null, never zero.</summary>
public sealed record BackfillBar(
    DateTimeOffset Ts,
    Guid UnderlyingId,
    char ExpiryFlag,
    int ExpiryCode,
    int Offset,
    string Side,
    int IntervalMinutes,
    int StrikePx,
    int SpotPx,
    int OpenPx,
    int HighPx,
    int LowPx,
    int ClosePx,
    long? Volume,
    long? Oi,
    float? Iv,
    short Flags);

/// <summary>The row of one attempt (<c>options.fetch_log</c>). <see cref="Bars"/> is the number of bars kept from the answer.</summary>
public sealed record BackfillFetchRecord(BackfillKey Key, int Attempt, DateTimeOffset StartedAt, DateTimeOffset EndedAt, string Outcome, int? HttpStatus, string? DhanCode, int Bars, int LatencyMs);

/// <summary>Where the backfill stands: the keys the plan has, the finished ones (of which the empty ones and the ones Dhan had no data for), the ones given up and the ones left.</summary>
public sealed record BackfillProgress(int Planned, int Done, int Empty, int NoData, int GivenUp, int Left);
