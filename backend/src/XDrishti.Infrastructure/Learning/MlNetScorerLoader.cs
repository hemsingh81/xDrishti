using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;
using XDrishti.Learning.MlNet;

namespace XDrishti.Infrastructure.Learning;

/// <summary>The scorer port over the ML.NET adapter (spec P6-07, ADR 0070): always the loader that requires the hash. <c>LoadUnverified</c> is never used here.</summary>
internal sealed class MlNetScorerLoader : IModelScorerLoader
{
    public Result<IExplainableScorer> Load(byte[] bytes, string sha256)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        ArgumentNullException.ThrowIfNull(sha256);
        var loaded = MlNetModel.Load(bytes, sha256);
        return loaded.IsFailure ? Result.Failure<IExplainableScorer>(loaded.Error) : Result.Success<IExplainableScorer>(loaded.Value);
    }
}
