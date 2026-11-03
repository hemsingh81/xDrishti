using System.Globalization;
using System.Text;
using XDrishti.Domain.Learning.Extensions;
using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// One formation (P14-13): the table of every candidate, the Benjamini-Hochberg outcome (<c>M</c> counts the unavailable tests), the pairs selected for the quarter and the counts the record keeps. An unavailable test counts in <c>M</c> with p = 1 and is never selected.
/// </summary>
public sealed class FormationResult
{
    /// <summary>How many rows of the table the stored detail keeps (the smallest p-values first).</summary>
    public const int DetailRows = 50;

    internal FormationResult(IReadOnlyList<PairTestRow> rows, IReadOnlyList<SelectedPair> selected, int rejected, int m, int unavailable, int halfLifeOutOfRange, int betaNotPositive)
    {
        Rows = rows;
        Selected = selected;
        Rejected = rejected;
        M = m;
        Unavailable = unavailable;
        HalfLifeOutOfRange = halfLifeOutOfRange;
        BetaNotPositive = betaNotPositive;
    }

    /// <summary>Every candidate, ordered by p-value (an unavailable test last), ties by input order.</summary>
    public IReadOnlyList<PairTestRow> Rows { get; }

    public IReadOnlyList<SelectedPair> Selected { get; }

    /// <summary>The Benjamini-Hochberg rejections.</summary>
    public int Rejected { get; }

    /// <summary>The tests counted: all candidates.</summary>
    public int M { get; }

    /// <summary>Tests with no statistic (counted in <see cref="M"/> with p = 1).</summary>
    public int Unavailable { get; }

    /// <summary>Rejected pairs whose half-life was missing or outside the range.</summary>
    public int HalfLifeOutOfRange { get; }

    /// <summary>Rejected pairs with a hedge ratio that is not above 0.</summary>
    public int BetaNotPositive { get; }

    /// <summary>Tests that gave a statistic.</summary>
    public int Tested => M - Unavailable;

    /// <summary>The table as text lines (rank, pair, tau, p, threshold, rejected, half-life, selected): the Benjamini-Hochberg table printed.</summary>
    public IReadOnlyList<string> Table()
    {
        var c = CultureInfo.InvariantCulture;
        var lines = new List<string>(Rows.Count + 1) { string.Create(c, $"rank pair tau p threshold(rank*q/{M}) rejected half_life selected") };
        foreach (var r in Rows)
        {
            var sb = new StringBuilder();
            sb.Append(c, $"{r.Rank} {r.Y}|{r.X} {Show(r.Tau)} {(r.PValue is { } p ? p.ToString("F6", c) : "p=1 (" + r.Reason + ")")} {r.Threshold.ToString("F6", c)} {(r.Rejected ? "yes" : "no")} {(r.HalfLife is { } h ? h.ToString("F2", c) : r.HalfLifeReason ?? "-")} {(r.Selected ? "selected" : r.NotSelected ?? "-")}");
            lines.Add(sb.ToString());
        }

        return lines;
    }

    /// <summary>The stored detail (jsonb): the counts, the selected pairs with their hedge ratios and half-lives, the half-lives, and the first <see cref="DetailRows"/> rows of the table. A value that is not available is the text <c>unavailable</c>.</summary>
    public string DetailJson(string sectorLabel, string asmGsmLabel, string banListLabel, IReadOnlyDictionary<string, int> exclusions)
    {
        ArgumentNullException.ThrowIfNull(exclusions);
        using var json = new ExtensionJson();
        json.Object().Count("m", M).Count("unavailable", Unavailable).Count("rejected", Rejected).Count("selected", Selected.Count).Count("half_life_out_of_range", HalfLifeOutOfRange).Count("beta_not_positive", BetaNotPositive)
            .Text("p_value_basis", PairsLabels.PValueAsymptotic).Text("sector", sectorLabel).Text("asm_gsm", asmGsmLabel).Text("ban_list", banListLabel).Object("exclusions");
        foreach (var pair in exclusions.OrderBy(e => e.Key, StringComparer.Ordinal))
        {
            json.Count(pair.Key, pair.Value);
        }

        json.EndObject().Array("half_lives");
        foreach (var s in Selected)
        {
            json.Object().Text("pair", s.Key).Number("sessions", s.HalfLifeSessions).EndObject();
        }

        json.EndArray().Array("selected_pairs");
        foreach (var s in Selected)
        {
            json.Object().Text("y", s.Y).Text("x", s.X).Number("alpha", s.Alpha).Number("beta", s.Beta).Number("spread_mean", s.SpreadMean).Number("half_life_sessions", s.HalfLifeSessions).Number("tau", s.Tau).Number("p", s.PValue).EndObject();
        }

        json.EndArray().Array("table");
        foreach (var r in Rows.Take(DetailRows))
        {
            json.Object().Count("rank", r.Rank).Text("y", r.Y).Text("x", r.X).Number("tau", r.Tau).Number("p", r.PValue).Text("reason", r.Reason ?? "none").Number("threshold", r.Threshold).Flag("rejected", r.Rejected).Number("half_life", r.HalfLife).Flag("selected", r.Selected).EndObject();
        }

        json.EndArray().EndObject();
        return json.Finish();
    }

    private static string Show(double? v) => v is { } x ? x.ToString("F4", CultureInfo.InvariantCulture) : "unavailable";
}
