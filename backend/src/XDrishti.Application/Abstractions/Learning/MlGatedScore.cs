namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A model's probability as the gate hands it out (CF-10 part 10c-2): present only when the gate allowed it.</summary>
/// <param name="Answer">The gate's answer.</param>
/// <param name="PWin">The model's P(win), or null whenever the answer is a refusal or the model could not be scored.</param>
public sealed record MlGatedScore(MlInfluenceAnswer Answer, double? PWin);
