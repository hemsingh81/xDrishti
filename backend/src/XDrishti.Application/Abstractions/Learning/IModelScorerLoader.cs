using XDrishti.Domain.Common;
using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The scorer port (P6-07, ADR 0070): turns the bytes of a registered model into a scorer, only if they hash to the recorded SHA-256. Domain types in and out.</summary>
public interface IModelScorerLoader
{
    /// <summary>Loads the model; a hash that does not match or bytes that are not a model are a failure with a code.</summary>
    Result<IExplainableScorer> Load(byte[] bytes, string sha256);
}
