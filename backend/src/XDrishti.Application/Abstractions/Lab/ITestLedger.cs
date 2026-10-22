namespace XDrishti.Application.Abstractions.Lab;

/// <summary>One row of the test ledger (spec P5-03): a Lab run that was created for a strategy family.</summary>
public sealed record LedgerRow(
    Guid RunId,
    string RunKey,
    string StrategyId,
    int Version,
    string StrategyHash,
    string? ParentHash,
    string Universe,
    DateOnly From,
    DateOnly To,
    string Method,
    DateTimeOffset CreatedAt);

/// <summary>
/// The per-family count of looks (spec P5-03, ADR 0059): <c>lab.test_ledger</c> behind a port. Insert-only for the runtime role; a row stays whatever happens to its run.
/// </summary>
public interface ITestLedger
{
    /// <summary>Appends the row; a second append of the same run id writes nothing and returns false.</summary>
    ValueTask<bool> AppendAsync(LedgerRow row, CancellationToken cancellationToken);

    /// <summary>The distinct strategy hashes recorded for the family: its variant count.</summary>
    ValueTask<int> CountVariantsAsync(string strategyId, CancellationToken cancellationToken);

    /// <summary>The rows of the family, newest first.</summary>
    ValueTask<IReadOnlyList<LedgerRow>> ListAsync(string strategyId, CancellationToken cancellationToken);
}
