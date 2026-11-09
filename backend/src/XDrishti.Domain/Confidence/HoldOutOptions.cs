using System.Globalization;

namespace XDrishti.Domain.Confidence;

/// <summary>
/// The <c>Confidence:HoldOut</c> settings of CF-06 (ADR 0207); every number is a proposal of the spec (D4, D5), a setting and not a constant.
/// </summary>
/// <param name="Enforce">The owner's switch (default false). The block is fixed at the first Lab post and a default window always ends before it. Off, an explicit window may reach the hold-out and a missing hold-out caps nothing; on, such a window is refused, and a full or robust PASS without a kept hold-out is capped at MARGINAL.</param>
/// <param name="AutoUnlock">The auto option (default false): on, the unlock runs once by itself when every development row is a pass; recorded as auto, never as an owner approval.</param>
/// <param name="MinMonths">Shortest hold-out in months (default 6; 1..60).</param>
/// <param name="SharePercent">Share of the stored history kept (default 20; 5..50).</param>
/// <param name="MinTrades">Fewest hold-out trades for a statement (default 30; 5..1000).</param>
/// <param name="Retention">Share of the development expectancy the hold-out must keep (default 0.5; 0.1..1).</param>
/// <param name="TrainingEmbargoDays">Calendar days left empty between the end of a training window and the hold-out start (default 14; 7..90; ADR 0243, AU-11): a trade held across the cut or a feature built on a trailing window must not carry hold-out information into training. 14 days covers the longest holding period the simulator allows plus a margin, as embargoes in purged cross-validation are at least the label horizon. Not part of <see cref="KeyText"/>: it does not size the hold-out.</param>
public sealed record HoldOutOptions(bool Enforce = false, bool AutoUnlock = false, int MinMonths = 6, double SharePercent = 20, int MinTrades = 30, double Retention = 0.5, int TrainingEmbargoDays = 14)
{
    /// <summary>The text of the values, kept with every unlock so a stored result says which proposals it was judged by.</summary>
    public string KeyText() => string.Create(CultureInfo.InvariantCulture, $"holdout/enforce={(Enforce ? "on" : "off")}/auto_unlock={(AutoUnlock ? "on" : "off")}/min_months={MinMonths}/share={SharePercent:R}/min_trades={MinTrades}/retention={Retention:R}");

    /// <summary>The snapshot hash: the lower-case hex SHA-256 of <see cref="KeyText"/>.</summary>
    public string SnapshotHash() => Research.CanonicalText.Sha256Hex(KeyText());

    /// <summary>The problems of the values, empty when they are usable.</summary>
    public IReadOnlyList<string> Problems()
    {
        var problems = new List<string>();
        if (MinMonths is < 1 or > 60)
        {
            problems.Add("HoldOut:MinMonths must be 1 to 60");
        }

        if (!double.IsFinite(SharePercent) || SharePercent is < 5 or > 50)
        {
            problems.Add("HoldOut:SharePercent must be 5 to 50");
        }

        if (MinTrades is < 5 or > 1000)
        {
            problems.Add("HoldOut:MinTrades must be 5 to 1000");
        }

        if (!double.IsFinite(Retention) || Retention is < 0.1 or > 1)
        {
            problems.Add("HoldOut:Retention must be 0.1 to 1");
        }

        if (TrainingEmbargoDays is < 7 or > 90)
        {
            problems.Add("HoldOut:TrainingEmbargoDays must be 7 to 90");
        }

        return problems.AsReadOnly();
    }
}
