namespace XDrishti.Application.Abstractions.Shadow;

/// <summary>One model's score of one shadow candidate, to store with its session (CF-10e part 10e-1). The candidate is named by its trade key within the session; the store resolves the entry in the same transaction.</summary>
/// <param name="TradeKey">The trade key of the candidate.</param>
/// <param name="ModelId">The registered model.</param>
/// <param name="ModelSha256">The model file hash.</param>
/// <param name="FeatureVersion">The feature catalog version of the vector.</param>
/// <param name="PWin">The probability, in [0, 1].</param>
/// <param name="FeaturesHash">SHA-256 (hex) of the feature vector the model read.</param>
public sealed record NewShadowScore(string TradeKey, long ModelId, string ModelSha256, string FeatureVersion, double PWin, string FeaturesHash);
