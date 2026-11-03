namespace XDrishti.Domain.Pairs;

/// <summary>The OLS fit of <c>ln y</c> on <c>ln x</c>: <c>y = Alpha + Beta x + e</c>, the residuals in index order (the spread), R squared, the number of points, the residual sum of squares and the total sum of squares of y.</summary>
public sealed record OlsFit(double Alpha, double Beta, IReadOnlyList<double> Residuals, double RSquared, int N, double Sse, double Sst);
