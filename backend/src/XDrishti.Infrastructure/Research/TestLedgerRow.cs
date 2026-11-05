namespace XDrishti.Infrastructure.Research;

/// <summary>A row of <c>lab.test_ledger</c> (spec P5-03, ADR 0059). It exists for the model and the migration; the store reads and writes it with SQL.</summary>
internal sealed class TestLedgerRow
{
    public Guid Id { get; set; }

    public string StrategyId { get; set; } = string.Empty;

    public int Version { get; set; }

    public string StrategyHash { get; set; } = string.Empty;

    public string? ParentHash { get; set; }

    public Guid LabRunId { get; set; }

    public string RunKey { get; set; } = string.Empty;

    public string UniverseLabel { get; set; } = string.Empty;

    public DateOnly WindowFrom { get; set; }

    public DateOnly WindowTo { get; set; }

    public string Method { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary><c>run</c>, <c>replan</c> (CF-06) or <c>null</c> (CF-07).</summary>
    public string TrialKind { get; set; } = "run";

    /// <summary>Extra candidate exits the first full test of the hash walked (CF-06, D3).</summary>
    public int ExtraTrials { get; set; }
}
