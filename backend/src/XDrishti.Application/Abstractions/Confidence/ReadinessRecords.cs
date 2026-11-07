namespace XDrishti.Application.Abstractions.Confidence;

/// <summary>The setting <c>confidence.readiness.enforce</c> as stored: the newest row of <c>readiness.settings</c>, OFF when there is none (spec CF-11 D6, ADR 0221).</summary>
/// <param name="Id">The row, or null when none exists.</param>
/// <param name="Enforce">Whether a failed gate refuses a lifecycle move (ON) or only warns (OFF).</param>
public sealed record ReadinessSwitch(long? Id, bool Enforce);

/// <summary>Reads the owner's switch <c>confidence.readiness.enforce</c>.</summary>
public interface IReadinessSettings
{
    /// <summary>The switch now; OFF with no id when no row is stored. May throw when the store cannot be read: the gate reads that as ON (the stricter reading).</summary>
    ValueTask<ReadinessSwitch> CurrentAsync(CancellationToken cancellationToken);
}

/// <summary>Appends a change of the owner's switch (insert only). Only the owner's handler calls it.</summary>
public interface IReadinessSwitchStore
{
    /// <summary>Appends a row (the database stamps its id in commit order and its time) and returns the id.</summary>
    ValueTask<long> AppendAsync(bool enforce, string actor, string reason, CancellationToken cancellationToken);
}

/// <summary>A scorecard snapshot to store (spec CF-11 D7): a daily one for the trend, or the one a gate use cites.</summary>
/// <param name="Subject"><c>strategy</c> or <c>system</c>.</param>
/// <param name="StrategyId">The strategy of a strategy snapshot; null for the system.</param>
/// <param name="Version">The strategy version of a strategy snapshot; null for the system or when the strategy has no version.</param>
/// <param name="TakenOn">The India date.</param>
/// <param name="Scope"><c>daily</c> (one per subject and day) or <c>gate</c> (one per gate use).</param>
/// <param name="Score">The readiness score, 0 to 100.</param>
/// <param name="Label">The label code (<c>not_ready</c>, <c>building</c>, <c>usable</c>, <c>ready</c>).</param>
/// <param name="Body">The whole scorecard as JSON, so a decision can cite exactly what was true.</param>
/// <param name="SettingsHash">The SHA-256 of the readiness settings that produced it.</param>
public sealed record NewReadinessSnapshot(string Subject, string? StrategyId, int? Version, DateOnly TakenOn, string Scope, double Score, string Label, string Body, string SettingsHash);

/// <summary>A stored snapshot without its body (the trend).</summary>
/// <param name="Id">The row.</param>
/// <param name="Subject"><c>strategy</c> or <c>system</c>.</param>
/// <param name="StrategyId">The strategy, or null for the system.</param>
/// <param name="Version">The version, or null.</param>
/// <param name="TakenOn">The India date.</param>
/// <param name="TakenAt">When it was stored (the database clock).</param>
/// <param name="Scope"><c>daily</c> or <c>gate</c>.</param>
/// <param name="Score">The score.</param>
/// <param name="Label">The label code.</param>
/// <param name="SettingsHash">The settings hash.</param>
public sealed record ReadinessSnapshotRow(long Id, string Subject, string? StrategyId, int? Version, DateOnly TakenOn, DateTimeOffset TakenAt, string Scope, double Score, string Label, string SettingsHash);

/// <summary>Writes scorecard snapshots. Insert only; nothing is ever changed or deleted.</summary>
public interface IReadinessSnapshotStore
{
    /// <summary>Stores a snapshot; null when a daily snapshot of the same subject and day exists already (idempotent).</summary>
    ValueTask<long?> InsertAsync(NewReadinessSnapshot snapshot, CancellationToken cancellationToken);
}

/// <summary>Reads scorecard snapshots. Read only.</summary>
public interface IReadinessSnapshotReader
{
    /// <summary>The newest snapshots first of one subject, optionally of one scope.</summary>
    ValueTask<IReadOnlyList<ReadinessSnapshotRow>> RecentAsync(string subject, string? strategyId, string? scope, int limit, CancellationToken cancellationToken);
}

/// <summary>A reading from a source that may not exist: a value, or null with the reason it is unavailable (never a pass).</summary>
/// <typeparam name="T">The value.</typeparam>
/// <param name="Value">The value, or null when unavailable.</param>
/// <param name="Reason">Why it is unavailable, or a short note on the source when it is not.</param>
public sealed record ReadinessReading<T>(T? Value, string Reason)
    where T : struct;

/// <summary>
/// The inputs of the scorecard that have no producer yet (spec CF-11, ADR 0221). There is one implementation and it says <c>unavailable</c> with the reason for every one of them (<c>UnavailableReadinessGaps</c>); when a producer is built it is added behind this port and the
/// scorecard changes by itself, with no change to the calculator. Read only. Only the scorecard assembler reads it (an architecture test).
/// </summary>
public interface IReadinessGaps
{
    /// <summary>Row 13: the strategy beats the same-universe benchmark net, risk-adjusted (P8-07 baskets benchmarks).</summary>
    ValueTask<ReadinessReading<bool>> BenchmarkBeatenAsync(string strategy, int? version, CancellationToken cancellationToken);

    /// <summary>Row 15: the owner's 20-trade check (P3-09) passed. It is the owner's signed record, which has no machine-readable store.</summary>
    ValueTask<ReadinessReading<bool>> TwentyTradeCheckPassedAsync(string strategy, CancellationToken cancellationToken);

    /// <summary>The hard fail "integrity fault": true when a P7 or P10 integrity check fails (the list of checks is unverified, so no source exists).</summary>
    ValueTask<ReadinessReading<bool>> IntegrityFaultAsync(CancellationToken cancellationToken);

    /// <summary>The hard fail "evidence only from tier C": true when all the evidence is tier C (the Lab verdict now carries the tier counts of its trades, but whether this means the Lab or the forward trades is an open owner question, ADR 0223).</summary>
    ValueTask<ReadinessReading<bool>> TierCOnlyAsync(string strategy, CancellationToken cancellationToken);

    /// <summary>The planned risk share of a strategy, 0 to 1 (the plan does not expose it per strategy yet; the system level then uses equal weights, labelled).</summary>
    ValueTask<ReadinessReading<double>> RiskShareAsync(string strategy, CancellationToken cancellationToken);
}
