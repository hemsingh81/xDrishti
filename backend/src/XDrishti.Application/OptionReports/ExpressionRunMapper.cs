using System.Text.Json;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>The pure mapping of a stored expression run to its summary: shares of the paired trades, each <c>unavailable</c> with <c>no_pairs</c> when nothing paired.</summary>
internal static class ExpressionRunMapper
{
    /// <summary>The lot-feasibility words in the order they are listed (the closed list of the store).</summary>
    public static IReadOnlyList<string> FeasibilityWords { get; } = ["sizable", "not_sizable", "lot_unknown", "delta_unavailable", "risk_invalid", "n/a"];

    public static ExpressionRunSummary ToSummary(ExpressionRunStored run)
    {
        ArgumentNullException.ThrowIfNull(run);
        return new ExpressionRunSummary(
            run.RunId,
            run.CashRunKey,
            run.ProfileHash,
            run.RemarkVersion,
            run.CreatedAt,
            run.Trades,
            run.Paired,
            Unpaired(run.UnpairedJson),
            run.Paired == 0 || run.BsFallbackShare is null ? ReportedShare.None(ReportStates.Reasons.NoPairs) : ReportedShare.Of(run.BsFallbackShare.Value),
            Share(run.DerivedExpiry, run.Paired),
            Share(run.SpreadFromRecentArchive, run.Paired),
            [.. FeasibilityWords.Select(w => new LotFeasibilityView(w, run.Sizable.GetValueOrDefault(w), Share(run.Sizable.GetValueOrDefault(w), run.Paired)))],
            run.Labels);
    }

    private static ReportedShare Share(int count, int paired) => paired == 0 ? ReportedShare.None(ReportStates.Reasons.NoPairs) : ReportedShare.Of((double)count / paired);

    private static List<UnpairedCountView> Unpaired(string json)
    {
        var list = new List<UnpairedCountView>();
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in document.RootElement.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
            {
                if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out var count))
                {
                    list.Add(new UnpairedCountView(property.Name, count));
                }
            }
        }

        return list;
    }
}
