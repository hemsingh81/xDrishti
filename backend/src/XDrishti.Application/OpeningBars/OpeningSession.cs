namespace XDrishti.Application.OpeningBars;

/// <summary>A share of the day's recording set: the instrument, Dhan's security id and the canonical symbol (kept for the supervisor only, never logged).</summary>
internal sealed record OpeningSetEntry(Guid InstrumentId, int SecurityId, string Symbol);

/// <summary>How a recording session ended.</summary>
internal enum OpeningSessionEnd
{
    /// <summary>Cancelled: a gate closed or the process is stopping.</summary>
    Stopped,

    /// <summary><c>OpeningBars:RecordUntil</c> was reached.</summary>
    Until,

    /// <summary>The free space went under the shared floor.</summary>
    LowDisk,

    /// <summary>Dhan refused the Data account's token.</summary>
    TokenRefused,

    /// <summary>The Data account has no usable token.</summary>
    NoToken,

    /// <summary>The median exchange-to-receive skew of the packets was over the limit: the run stopped.</summary>
    ClockSkew,

    /// <summary>Dhan answered 806 (the Data APIs are not part of the plan) or 805 (too many connections), or more than 10 reconnects in 5 minutes.</summary>
    Refused,
}

/// <summary>What one session recorded: the figures the Services screen shows for the last day.</summary>
internal sealed record OpeningRunStats(int Instruments, long Packets, long Bars, long Missing, long Late, long Duplicates, int Gaps, long Outside, int SkewSeconds);

internal sealed record OpeningSessionResult(OpeningSessionEnd End, OpeningRunStats Stats);
