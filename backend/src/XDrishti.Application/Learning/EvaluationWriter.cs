using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Learning;

/// <summary>The two writes every protocol makes (P15-10): the ledger row of a trial, always before its numbers are computed, and the insert-only record of a verdict. The record's creation time is the clock's.</summary>
internal sealed class EvaluationWriter(IExtensionEvaluationStore store, ITestLedger ledger, TimeProvider clock)
{
    private const string Universe = "stored runs";

    /// <summary>Appends the trial's row (a second append of the same run id writes nothing) and returns N: the distinct trials of the family.</summary>
    public async ValueTask<int> AppendTrialAsync(ExtensionTrialId trial, string runKey, DateOnly from, DateOnly to, string method, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(trial);
        _ = await ledger.AppendAsync(new LedgerRow(trial.RunId, runKey, trial.Family, 1, trial.Hash, null, Universe, from, to, method, clock.GetUtcNow()), cancellationToken);
        return await ledger.CountVariantsAsync(trial.Family, cancellationToken);
    }

    /// <summary>N for a family whose evaluation counts no trial: the ledger's count, at least 1.</summary>
    public async ValueTask<int> CountAsync(string family, CancellationToken cancellationToken) => Math.Max(1, await ledger.CountVariantsAsync(family, cancellationToken));

    /// <summary>Stores the record; a failure is the hold-out refusal.</summary>
    public async ValueTask<Result<EvaluatedRecord>> StoreAsync(ExtensionEvaluationRecord record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        var written = await store.AppendAsync(record, clock.GetUtcNow(), cancellationToken);
        return written.IsFailure
            ? Result.Failure<EvaluatedRecord>(written.Error)
            : Result.Success(new EvaluatedRecord(record.EvaluationId, record.Protocol, record.VersionKey, record.Verdict, record.Missing, written.Value, record.HoldOutRead));
    }
}
