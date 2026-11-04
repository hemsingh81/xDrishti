namespace XDrishti.Domain.Lab;

/// <summary>The status words of a hold-out result.</summary>
public static class HoldOutStatus
{
    /// <summary>The rule is met.</summary>
    public const string Pass = "pass";

    /// <summary>The rule is not met.</summary>
    public const string Fail = "fail";

    /// <summary>No statement: too few trades, or no positive development expectancy to compare with. Never a pass.</summary>
    public const string Inconclusive = "inconclusive";
}

/// <summary>
/// What the one look at the hold-out said (spec CF-06, D5). <paramref name="Passed"/> is true only for a first, fresh look that meets the rule; a re-used window shows its numbers and counts as a trial, never as a pass.
/// </summary>
/// <param name="Status">pass, fail or inconclusive.</param>
/// <param name="Passed">Whether this result counts toward a pass.</param>
/// <param name="Ratio">Hold-out expectancy over development out-of-sample expectancy; null when there is no positive development figure.</param>
/// <param name="Reason">In words, why.</param>
public sealed record HoldOutResult(string Status, bool Passed, double? Ratio, string Reason);
