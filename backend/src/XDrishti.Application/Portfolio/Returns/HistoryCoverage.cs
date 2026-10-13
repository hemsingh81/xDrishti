namespace XDrishti.Application.Portfolio.Returns;

/// <summary>
/// How much of the open holdings the history covers: <c>Holdings</c> open holdings, <c>Covered</c> of them have stored day bars, <c>Unpriced</c> have none (not tracked, not in the instrument master, or bars not fetched yet:
/// they are left out and never valued at zero). <c>LateStart</c> counts covered holdings whose bars begin after their earliest lot, so part of their history is missing.
/// </summary>
public sealed record HistoryCoverage(int Holdings, int Covered, int Unpriced, int LateStart);
