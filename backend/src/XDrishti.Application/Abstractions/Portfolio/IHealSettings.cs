namespace XDrishti.Application.Abstractions.Portfolio;

/// <summary>Deployment settings of the self-healing syncs (<c>Portfolio:Heal</c>, ADR 0027): the back-off of the quiet retries, the days of failure after which the owner is told, and how far back the history is deepened.</summary>
public interface IHealSettings
{
    /// <summary>The waits before each retry, in order (default 1, 5 and 15 minutes): their number is the number of retries of one occurrence. Empty switches the retries off.</summary>
    IReadOnlyList<TimeSpan> RetryBackoff { get; }

    /// <summary>The consecutive failed days after which a failing sync becomes an item (default 2). Reasons only the owner can fix are told at once.</summary>
    int PersistDays { get; }

    /// <summary>How many years back from today the nightly deepening reads at most (default 8); 0 switches it off.</summary>
    int DeepenMaxYears { get; }

    /// <summary>How many days a resolved attention item is kept before the healer tick deletes it (default 30, ADR 0030).</summary>
    int AttentionRetentionDays { get; }
}
