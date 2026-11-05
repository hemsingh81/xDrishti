namespace XDrishti.Domain.Learning;

/// <summary>One point of the learning curve (spec CF-12, D5): the out-of-time gain of a model refit on the first <paramref name="Rows"/> development rows, measured on one fixed later test block.</summary>
/// <param name="Rows">Rows the model was refit on.</param>
/// <param name="Gain">Base-rate log loss minus model log loss on the fixed block (positive is better).</param>
public sealed record LearningCurvePoint(int Rows, double Gain);
