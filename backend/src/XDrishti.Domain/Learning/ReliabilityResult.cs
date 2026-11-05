namespace XDrishti.Domain.Learning;

/// <summary>
/// The reliability slope of a model's probabilities (spec CF-12, D6; ADR 0218): the coefficient of the logistic regression of the outcome on <c>logit(p_hat)</c>. Ideal 1; below 1 means the model is overconfident, above 1 underconfident.
/// </summary>
/// <param name="State">The reading.</param>
/// <param name="Outcomes">Outcomes in the fit (the trailing window, or all there are below it).</param>
/// <param name="Slope">The fitted slope; null when unavailable.</param>
/// <param name="Intercept">The fitted intercept; null when unavailable.</param>
/// <param name="StandardError">The standard error of the slope; null when unavailable.</param>
/// <param name="Text">The words, with the numbers.</param>
public sealed record ReliabilityResult(ReliabilityState State, int Outcomes, double? Slope, double? Intercept, double? StandardError, string Text);
