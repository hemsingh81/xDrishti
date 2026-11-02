using XDrishti.Domain.Common;
using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where the evaluations of feature extensions are kept (P13-05): <c>research.extension_evaluations</c> behind a port. Insert-only; no decision path reads it.</summary>
public interface IExtensionEvaluationStore
{
    /// <summary>
    /// Writes the record. True when written; false when the same evaluation id is stored already (nothing changes). A second record that reads the hold-out for the same family and version key is refused with the error <c>extensions.holdout_used</c>.
    /// </summary>
    ValueTask<Result<bool>> AppendAsync(ExtensionEvaluationRecord record, DateTimeOffset createdAt, CancellationToken cancellationToken);

    /// <summary>The record with this id, or null.</summary>
    ValueTask<ExtensionEvaluationRecord?> GetAsync(string evaluationId, CancellationToken cancellationToken);

    /// <summary>The newest records of the family first.</summary>
    ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken);

    /// <summary>Whether the hold-out has been read for this family and version key.</summary>
    ValueTask<bool> HoldOutUsedAsync(string family, string versionKey, CancellationToken cancellationToken);
}
