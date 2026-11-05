using XDrishti.Domain.Learning;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The gate's answer for one model (CF-10 part 10c-2): may it influence a decision, and every reason it may not.</summary>
/// <param name="ModelId">The registry id asked about.</param>
/// <param name="MayInfluence">True only when the predicate <see cref="MlInfluence"/> holds and the model is the current champion. The default of every refusal is false.</param>
/// <param name="Reasons">Why not; empty only when allowed.</param>
public sealed record MlInfluenceAnswer(long ModelId, bool MayInfluence, IReadOnlyList<MlInfluenceReason> Reasons)
{
    /// <summary>A refusal with one reason.</summary>
    public static MlInfluenceAnswer Refused(long modelId, string code, string text) => new(modelId, false, [new MlInfluenceReason(code, text)]);
}
