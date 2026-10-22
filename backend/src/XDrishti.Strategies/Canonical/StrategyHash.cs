using XDrishti.Domain.Research;
using XDrishti.Strategies.Model;

namespace XDrishti.Strategies.Canonical;

/// <summary>The hash of a strategy version (P3-02c D9): the SHA-256 of the UTF-8 bytes of <see cref="CanonicalStrategy.Json"/>, 64 lowercase hex characters. The id is inside the text, the envelope (<c>version</c>, <c>test</c>, <c>universe</c>) is not.</summary>
public static class StrategyHash
{
    /// <summary>The hash of the definition.</summary>
    public static string Of(StrategyDefinition definition) => CanonicalText.Sha256Hex(CanonicalStrategy.Json(definition));
}
