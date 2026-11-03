namespace XDrishti.Domain.Options.Evaluation;

/// <summary>A test window of the cash run's walk-forward, for one option side (<c>CE</c> for the buys, <c>PE</c> for the intraday sells): the entries dated from <paramref name="Start"/> to <paramref name="End"/>, both included, are out of sample.</summary>
public sealed record ExpressionWindow(string Side, DateOnly Start, DateOnly End);
