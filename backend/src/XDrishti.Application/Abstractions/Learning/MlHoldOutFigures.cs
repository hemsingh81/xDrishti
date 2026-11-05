namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The figures of a model's one look at the locked hold-out (CF-10 part 10c-2). A null or not finite figure is not available.</summary>
/// <param name="Rows">Hold-out rows the model was scored on.</param>
/// <param name="ProbabilityBeatsNoMl">The probability, by the paired day-cluster bootstrap, that the model beats the best no-ML baseline there.</param>
/// <param name="PolicyEffectR">The effect of the take rule of P6-13 on realised R there.</param>
public sealed record MlHoldOutFigures(int? Rows, double? ProbabilityBeatsNoMl, double? PolicyEffectR);
