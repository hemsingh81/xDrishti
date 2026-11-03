using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The read side of the stored evaluations (P15-12): what a read-only check may hold. <see cref="IExtensionEvaluationStore"/> adds the write.</summary>
public interface IExtensionEvaluationReader
{
    /// <summary>The newest records of the family first.</summary>
    ValueTask<IReadOnlyList<ExtensionEvaluationRecord>> ListAsync(string family, int limit, CancellationToken cancellationToken);
}
