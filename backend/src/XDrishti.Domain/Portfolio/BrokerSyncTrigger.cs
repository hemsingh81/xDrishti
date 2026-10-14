namespace XDrishti.Domain.Portfolio;

/// <summary>Who started a broker history run (ADR 0025). Stored by name (lower case) — never rename a value.</summary>
public enum BrokerSyncTrigger
{
    /// <summary>The owner pressed "Sync now".</summary>
    Manual,

    /// <summary>The scheduled service, incrementally from the watermark.</summary>
    Scheduled,

    /// <summary>The scheduled service, for an account that was never read before: the backfill from the first import date.</summary>
    First,
}
