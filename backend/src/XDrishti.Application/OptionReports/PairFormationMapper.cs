using System.Text.Json;
using XDrishti.Application.Abstractions.OptionReports;
using XDrishti.Domain.Options.Reports;

namespace XDrishti.Application.OptionReports;

/// <summary>
/// The pure mapping of a stored formation to its summary. The threshold is the Benjamini-Hochberg line <c>rank * q / m</c> at the last rejected rank, read from the stored table (the rejected ranks come first); the half-lives are those of the selected pairs.
/// A threshold or a distribution that the record does not hold is <c>unavailable</c> with its reason, never a number.
/// </summary>
internal static class PairFormationMapper
{
    public static PairFormationSummary ToSummary(PairFormationStored f)
    {
        ArgumentNullException.ThrowIfNull(f);
        using var document = JsonDocument.Parse(f.DetailJson);
        var root = document.RootElement;
        return new PairFormationSummary(f.FormationId, f.Family, f.Quarter, f.WindowFrom, f.WindowTo, f.CreatedAt, f.Candidates, f.Tested, f.Unavailable, f.Selected, Threshold(root), HalfLives(root));
    }

    private static ReportedShare Threshold(JsonElement root)
    {
        if (!root.TryGetProperty("rejected", out var rejected) || rejected.ValueKind != JsonValueKind.Number || !rejected.TryGetInt32(out var count))
        {
            return ReportedShare.None(ReportStates.Reasons.NotRecorded);
        }

        if (count == 0)
        {
            return ReportedShare.None(ReportStates.Reasons.NoRejection);
        }

        if (root.TryGetProperty("table", out var table) && table.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in table.EnumerateArray())
            {
                if (row.TryGetProperty("rank", out var rank) && rank.ValueKind == JsonValueKind.Number && rank.TryGetInt32(out var r) && r == count
                    && row.TryGetProperty("threshold", out var threshold) && threshold.ValueKind == JsonValueKind.Number)
                {
                    return ReportedShare.Of(threshold.GetDouble());
                }
            }
        }

        return ReportedShare.None(ReportStates.Reasons.NotRecorded);
    }

    // Reads the same stored shape (half_lives[].sessions) as PairResearchStore.HalfLives in Infrastructure, which Application cannot call; keep the two in step.
    private static HalfLifeView HalfLives(JsonElement root)
    {
        var values = new List<double>();
        if (root.TryGetProperty("half_lives", out var array) && array.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in array.EnumerateArray())
            {
                if (item.TryGetProperty("sessions", out var sessions) && sessions.ValueKind == JsonValueKind.Number)
                {
                    values.Add(sessions.GetDouble());
                }
            }
        }

        if (values.Count == 0)
        {
            return new HalfLifeView(ReportStates.Unavailable, ReportStates.Reasons.NoPairs, null, null, null, null);
        }

        values.Sort();
        var middle = values.Count / 2;
        var median = values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
        return new HalfLifeView(ReportStates.Ok, null, values.Count, values[0], median, values[^1]);
    }
}
