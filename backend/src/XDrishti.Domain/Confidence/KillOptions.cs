using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Kill</c> settings of CF-12 part 12b (ADR 0219, spec D2): the two numbers of the strategy-level kill rules that may move. Both can only <b>tighten</b> the spec's proposals, so a kill fires no later than the spec says: the cost multiplier of rule (a)
/// is at least the spec's 1.5 (a higher multiplier makes <c>net - (m - 1) c</c> smaller, so the rule fires sooner) and the drawdown factor of rule (b) is at most the spec's 2 and at least 1 (a lower factor makes the limit smaller). The trailing 100 trades and the 20 new trades
/// between firings are trade-offs, not tighten-only values, so they are not settings. The automatic switch is not here: it is stored state that only an owner act changes. <see cref="Problems"/> lists every value out of range (the host refuses to start on one).
/// </summary>
/// <param name="StressMultiplier">The cost multiplier of rule (a) (default 1.5; 1.5 to 3).</param>
/// <param name="DrawdownFactor">The factor on the 95th percentile of the matched drawdown for rule (b) (default 2; 1 to 2).</param>
public sealed record KillOptions(double StressMultiplier = KillOptions.DefaultStressMultiplier, double DrawdownFactor = KillOptions.DefaultDrawdownFactor)
{
    /// <summary>The spec's proposal, which is also the lowest multiplier configuration may take.</summary>
    public const double DefaultStressMultiplier = 1.5;

    /// <summary>The spec's proposal, which is also the highest factor configuration may take.</summary>
    public const double DefaultDrawdownFactor = 2;

    /// <summary>The text of the values, kept with every proposal so it says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"stress={StressMultiplier:R}/drawdownFactor={DrawdownFactor:R}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!double.IsFinite(StressMultiplier) || StressMultiplier is < DefaultStressMultiplier or > 3)
        {
            problems.Add("Kill:StressMultiplier must be 1.5 to 3 (it can only be tightened)");
        }

        if (!double.IsFinite(DrawdownFactor) || DrawdownFactor is < 1 or > DefaultDrawdownFactor)
        {
            problems.Add("Kill:DrawdownFactor must be 1 to 2 (it can only be tightened)");
        }

        return problems.AsReadOnly();
    }
}
