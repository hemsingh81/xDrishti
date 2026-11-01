using XDrishti.Application.Abstractions.OptionChain;

namespace XDrishti.Application.OptionChain;

/// <summary>Why a recording session ended.</summary>
internal enum ChainSessionEnd
{
    /// <summary>It was cancelled: the window ended, a gate closed or the process is stopping.</summary>
    Stopped,

    /// <summary>Every slot of the day was done.</summary>
    Completed,

    /// <summary>Dhan refused the Data account's token (DH-901, 401, 403, 807, 809): nothing is sent until it changes.</summary>
    TokenRefused,

    /// <summary>Dhan answered DH-902 or 806: the Data APIs are not part of the plan: that ends the day.</summary>
    NotSubscribed,

    /// <summary>Three slots in a row met the rate limit: a pause of 15 minutes.</summary>
    RatePaused,

    /// <summary>The Data account has no usable token any more.</summary>
    NoToken,

    /// <summary>The stored size went over its cap.</summary>
    DiskBudget,

    /// <summary>The free space went under its floor.</summary>
    DiskFree,
}

/// <summary>The counts of a session: slots run, requests made, requests that were ok, gaps (every other planned request), snapshot rows newly stored.</summary>
internal sealed record ChainRunStats(int Slots, int Requests, int Ok, int Gaps, long Rows);

/// <summary>How a session ended and what it recorded.</summary>
internal sealed record ChainSessionResult(ChainSessionEnd End, ChainRunStats Stats);

/// <summary>One run's worth of recording: wait for the slot, ask, parse, store, guard the disk. The supervisor owns the gates, the window and the run record; the session owns the requests.</summary>
internal interface IChainSession
{
    /// <summary>The state word of the session now.</summary>
    string State { get; }

    /// <summary>The counts so far.</summary>
    ChainRunStats Stats { get; }

    /// <summary>False when the last disk look could not read the free space (only the size cap was in force).</summary>
    bool FreeSpaceReadable { get; }

    /// <summary>Runs until cancelled or until it cannot go on. Cancellation is reported as <see cref="ChainSessionEnd.Stopped"/>, not thrown.</summary>
    Task<ChainSessionResult> RunAsync(ChainDay day, CancellationToken cancellationToken);
}
