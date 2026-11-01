using XDrishti.Application.Abstractions.OptionBackfill;

namespace XDrishti.Application.OptionBackfill;

/// <summary>Why a backfill session ended.</summary>
internal enum BackfillSessionEnd
{
    /// <summary>It was cancelled: a gate closed or the process is stopping.</summary>
    Stopped,

    /// <summary>Every planned key is finished or given up.</summary>
    Completed,

    /// <summary>The night window (16:30 to 08:30 India time) ended.</summary>
    WindowEnd,

    /// <summary>Dhan refused the Data account's token (DH-901, 401, 403, 807, 809): nothing is sent until it changes.</summary>
    TokenRefused,

    /// <summary>Dhan answered DH-902 or 806: the Data APIs are not part of the plan.</summary>
    NotSubscribed,

    /// <summary>The Data account has no usable token any more.</summary>
    NoToken,

    /// <summary>The stored size went over its cap.</summary>
    DiskBudget,

    /// <summary>The free space went under its floor.</summary>
    DiskFree,

    /// <summary>The day's request cap was reached.</summary>
    DailyCap,

    /// <summary>Three answers in a row could not be stored: the database is away.</summary>
    StoreFailed,

    /// <summary>Five answers in a row were rate limited, across keys.</summary>
    RateLimited,

    /// <summary>Five keys in a row were given up: something is wrong that a key by key retry will not mend.</summary>
    TooManyGivenUp,

    /// <summary>Dhan answered DH-905: the request itself is wrong.</summary>
    BadRequest,
}

/// <summary>The counts of a session: requests sent, ok (bars stored), empty or no-data answers, gaps (failed attempts), keys given up, bars newly stored, and the requests sent on the current India day.</summary>
internal sealed record BackfillRunStats(int Requests, int Ok, int Empty, int Gaps, int GivenUp, long Bars, int RequestsToday);

/// <summary>How a session ended and what it did.</summary>
internal sealed record BackfillSessionResult(BackfillSessionEnd End, BackfillRunStats Stats);

/// <summary>One night's worth of fetching: the supervisor owns the gates, the window and the run record; the session owns the requests, the throttle spacing and the disk guard.</summary>
internal interface IBackfillSession
{
    /// <summary>The state word of the session now.</summary>
    string State { get; }

    /// <summary>The counts so far.</summary>
    BackfillRunStats Stats { get; }

    /// <summary>False when the last disk look could not read the free space (only the size cap was in force).</summary>
    bool FreeSpaceReadable { get; }

    /// <summary>Runs until cancelled or until it cannot go on. Cancellation is reported as <see cref="BackfillSessionEnd.Stopped"/>, not thrown.</summary>
    Task<BackfillSessionResult> RunAsync(IReadOnlyList<BackfillTarget> targets, CancellationToken cancellationToken);
}
