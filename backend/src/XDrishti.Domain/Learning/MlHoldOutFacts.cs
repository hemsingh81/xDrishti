namespace XDrishti.Domain.Learning;

/// <summary>What is known about a model on the locked hold-out (CF-10 part 10c-2, spec D6). A figure that is null or not finite is not available.</summary>
/// <param name="Kept">Whether the model was trained with a hold-out kept back.</param>
/// <param name="Reused">Whether the hold-out was spent already: its family took its one look (another model, or this one on a block that had been seen), so a further look is a new trial that cannot count.</param>
/// <param name="Rows">Hold-out rows the model was scored on.</param>
/// <param name="ProbabilityBeatsNoMl">The probability, by the paired day-cluster bootstrap, that the model's rung beats the best no-ML baseline (base rate or cell rate) there.</param>
/// <param name="PolicyEffectR">The effect on realised R of the take rule of P6-13 (mean R of the kept trades minus that of all trades); negative fails.</param>
public sealed record MlHoldOutFacts(bool Kept, bool Reused, int? Rows, double? ProbabilityBeatsNoMl, double? PolicyEffectR);
