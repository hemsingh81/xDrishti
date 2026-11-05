using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Nulls</c> settings of CF-07 (ADR 0208): the random-entry null of the Lab and whether it decides anything. Every number is a proposal of the spec (D1) and a setting, not a constant; <see cref="Problems"/> lists every value out of
/// its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing computed) or <see cref="ConfidenceMode.ReportOnly"/> (the default). <see cref="ConfidenceMode.Gate"/> is not a mode here: enforcing is <paramref name="Enforce"/>.</param>
/// <param name="Enforce">The owner's switch (default false). On, a null that is not a pass (a fail, or unavailable) caps a PASS at MARGINAL and never raises an outcome.</param>
/// <param name="Draws">Seeds, each one full set of random-entry trades (default 1000; 200..5000).</param>
/// <param name="Percentile">The percentile of the random means that is shown, and the level of the test: the real mean passes when <c>p</c> is at most <c>1 - Percentile / 100</c> (default 95; 90..99).</param>
/// <param name="Redraws">How many times a draw that makes no trade is drawn again before the trade is dropped and counted (default 5; 0..20).</param>
public sealed record NullOptions(ConfidenceMode Mode = ConfidenceMode.ReportOnly, bool Enforce = false, int Draws = 1000, double Percentile = 95, int Redraws = 5)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The level of the test: <c>1 - Percentile / 100</c>, rounded so that 95 gives exactly 0.05.</summary>
    public double Alpha => Math.Round(1 - (Percentile / 100), 10);

    /// <summary>The text of the values, kept with every figure so a stored document says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"{Mode}/enforce={(Enforce ? "on" : "off")}/draws={Draws}/percentile={Percentile:R}/redraws={Redraws}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("Nulls:Mode must be Off or ReportOnly (enforcing is Nulls:Enforce)");
        }

        if (Enforce && Mode == ConfidenceMode.Off)
        {
            problems.Add("Nulls:Enforce needs Nulls:Mode ReportOnly (an enforcing switch with nothing computed would cap nothing)");
        }

        if (Draws is < 200 or > 5000)
        {
            problems.Add("Nulls:Draws must be 200 to 5000");
        }

        if (!double.IsFinite(Percentile) || Percentile is < 90 or > 99)
        {
            problems.Add("Nulls:Percentile must be 90 to 99");
        }

        if (Redraws is < 0 or > 20)
        {
            problems.Add("Nulls:Redraws must be 0 to 20");
        }

        return problems.AsReadOnly();
    }
}
