using XDrishti.Application.Abstractions.Replay;
using XDrishti.Domain.Learning;
using XDrishti.Domain.Replay;

namespace XDrishti.Application.Replay.Reads;

/// <summary>The words and small derivations every replay read shares (SR-01 part C): the costs label, the hindsight sentences, completeness, the list row and the figures of a run.</summary>
internal static class ReplayReadSupport
{
    /// <summary>The label of a run without a charge sheet (AC 8, ISS-25), on every response.</summary>
    public const string GrossLabel = "before costs (charge sheet not filled, ISS-25)";

    private static readonly IReadOnlyDictionary<string, string> Sentences = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [HindsightLabels.VersionsStart] = "Hindsight: the strategies are today's active versions, written after the start date and run backwards.",
        [HindsightLabels.SettingsNow] = "Hindsight: the settings are today's, not those of each session.",
        [HindsightLabels.Survivorship] = "Hindsight: before a basket's known-since date the earliest known membership is used (survivorship).",
        [HindsightLabels.DataSeenBefore] = "Hindsight: the strategies were designed on this history.",
        [HindsightLabels.BeforeCosts] = GrossLabel,
        [HindsightLabels.LifecycleAsRecorded] = "Hindsight: learning mode used the cell lifecycle as recorded, not recomputed.",
        [HindsightLabels.FiltersWithoutHistory] = "The event, F&O-ban and surveillance filters were not applied: they have no history.",
        [HindsightLabels.ResearchCells] = "Cells in the research state were allowed to be candidates.",
    };

    public static ReplayParams? ParamsOf(ReplayRunRow run) => ReplayJson.Read<ReplayParams>(run.ParamsJson);

    public static string CostsLabel(ReplayParams? p) =>
        p?.CostSheetId is { } id ? "net of costs (charge sheet " + id + ")" : GrossLabel;

    public static string Basis(ReplayParams? p) => p?.CostSheetId is null ? "gross" : "net";

    public static IReadOnlyList<ReplayLabelView> Labels(ReplayParams? p, IEnumerable<string>? extra = null)
    {
        // without stored parameters the run is treated as gross (no sheet is known), so before_costs is always stated
        var codes = new SortedSet<string>(p is null ? [HindsightLabels.SettingsNow, HindsightLabels.DataSeenBefore, HindsightLabels.BeforeCosts] : HindsightLabels.For(p), StringComparer.Ordinal);
        if (extra is not null)
        {
            codes.UnionWith(extra);
        }

        return [.. codes.Select(c => new ReplayLabelView(c, Sentences.GetValueOrDefault(c, c)))];
    }

    public static IReadOnlyList<ReplayLabelView> Describe(IEnumerable<string> codes) => [.. codes.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Select(c => new ReplayLabelView(c, Sentences.GetValueOrDefault(c, c)))];

    public static ReplayCompleteness Completeness(ReplayRunRow run) => new(run.Complete, run.SessionsDone, run.SessionsTotal, run.CursorSession);

    public static ReplayRunListItem ListItem(ReplayRunRow run)
    {
        var p = ParamsOf(run);
        return new ReplayRunListItem(Labels(p), run.Id, run.PairId, run.CreatedAt, run.From, run.To, run.Mode, run.State, Completeness(run), run.LastEquity, run.LastDrawdown, run.TrialNumber, CostsLabel(p));
    }

    /// <summary>The strategy of a cell key (its first part).</summary>
    public static string Strategy(string cellKey) => CellId.TryParse(cellKey)?.Strategy ?? cellKey.Split('|')[0];
}
