namespace XDrishti.Domain.Learning;

/// <summary>The fold sign test of one comparison (CF-10 part 10c-1, spec D4): in how many folds the model beat the baseline.</summary>
/// <param name="Better">Folds with a positive gain.</param>
/// <param name="Worse">Folds with a negative gain.</param>
/// <param name="Ties">Folds with a gain of exactly zero, or a gain that is not a finite number (left out of the test, as the classic sign test does).</param>
/// <param name="PValue">The one-sided p-value, the chance of at least <paramref name="Better"/> better folds of <c>Better + Worse</c> if each were a coin toss; null when no fold counts or there are more than 1000.</param>
public sealed record SignTestResult(int Better, int Worse, int Ties, double? PValue)
{
    /// <summary>The folds that count.</summary>
    public int Folds => Better + Worse;
}
