using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:Plateau</c> settings of CF-07 part C (ADR 0210): the plateau gate on the swept parameters of a robust Lab run and whether it decides anything. Every number is a proposal of the spec (D4) and a setting, not a constant;
/// <see cref="Problems"/> lists every value out of its range (the host refuses to start on one).
/// </summary>
/// <param name="Mode"><see cref="ConfidenceMode.Off"/> (nothing computed) or <see cref="ConfidenceMode.ReportOnly"/> (the default). <see cref="ConfidenceMode.Gate"/> is not a mode here: enforcing is <paramref name="Enforce"/>.</param>
/// <param name="Enforce">The owner's switch (default false), separate from <c>Nulls:Enforce</c> (spec D5). On, a gate that is not a pass (a fail, or unavailable) caps a PASS at MARGINAL and never raises an outcome.</param>
/// <param name="Retention">The share of the base net expectancy each of the two points next to the declared value must keep (default 0.70; 0.50..0.95).</param>
/// <param name="MinPoints">The fewest valid points of a parameter, the declared value included, for a reading (default 3, the declared value and one on each side; 3..5, because a sweep has at most four steps).</param>
public sealed record PlateauOptions(ConfidenceMode Mode = ConfidenceMode.ReportOnly, bool Enforce = false, double Retention = 0.70, int MinPoints = 3)
{
    /// <summary>True when anything is computed.</summary>
    public bool IsOn => Mode != ConfidenceMode.Off;

    /// <summary>The text of the values, kept with every figure so a stored document says which settings produced it.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"{Mode}/enforce={(Enforce ? "on" : "off")}/retention={Retention:R}/minPoints={MinPoints}");

    /// <summary>The lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (!Enum.IsDefined(Mode) || Mode == ConfidenceMode.Gate)
        {
            problems.Add("Plateau:Mode must be Off or ReportOnly (enforcing is Plateau:Enforce)");
        }

        if (Enforce && Mode == ConfidenceMode.Off)
        {
            problems.Add("Plateau:Enforce needs Plateau:Mode ReportOnly (an enforcing switch with nothing computed would cap nothing)");
        }

        if (!double.IsFinite(Retention) || Retention is < 0.5 or > 0.95)
        {
            problems.Add("Plateau:Retention must be 0.50 to 0.95");
        }

        if (MinPoints is < 3 or > 5)
        {
            problems.Add("Plateau:MinPoints must be 3 to 5 (two neighbours and the declared value are the least; a sweep has at most five points)");
        }

        return problems.AsReadOnly();
    }
}
