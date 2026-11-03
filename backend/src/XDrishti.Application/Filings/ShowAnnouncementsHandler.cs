using System.Globalization;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>xd eval announcements show</c> (spec P15-07 criterion 9): the sample, the class counts, the label distribution per half and the baseline's precision on the labelled rows, with the plain statement that precision on this sample is a stratified estimate and the formula of the
/// population-weighted precision that the set D report prints (P15-08). Without labels it says it is waiting for them. Read only; counts and ids, never text.
/// </summary>
internal sealed class ShowAnnouncementsHandler(IAnnouncementEvalFiles files) : IQueryHandler<ShowAnnouncementsQuery, AnnouncementEvalReport>
{
    public const string Formula = "population-weighted precision of material = sum of w * [predicted material and labelled material] / sum of w * [predicted material], with w = eligible rows of the stratum / sampled rows of the stratum (sample.json)";

    public async ValueTask<AnnouncementEvalReport> HandleAsync(ShowAnnouncementsQuery query, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(query);
        var lines = new List<string>();
        var sampleText = await files.ReadAsync(AnnouncementFile.Sample, cancellationToken);
        var sample = sampleText is null ? null : AnnouncementSampleFile.Parse(sampleText);
        if (sample is null)
        {
            lines.Add("no sample yet: xd eval announcements sample --from YYYY-MM-DD --to YYYY-MM-DD --seed 1337");
            return new AnnouncementEvalReport(false, lines);
        }

        lines.Add($"sample: seed {sample.Seed}, sampler {sample.SamplerVersion}, {sample.From} to {sample.To}, {sample.MainCount} main and {sample.ReserveCount} reserve rows from {sample.Eligible} eligible (material categories {sample.EligibleMaterial}, others {sample.EligibleOther}); baseline {sample.BaselineSha256[..12]}");
        var labelled = await files.ReadAsync(AnnouncementFile.Labelled, cancellationToken);
        if (labelled is null)
        {
            lines.Add("waiting for labels: data/eval/sets/announcements.jsonl is not there; label data/eval/announcements/to-label.csv and run xd eval announcements import <file>. Set D stays not_evaluable.");
            return new AnnouncementEvalReport(false, lines);
        }

        var parsed = AnnouncementSet.Parse(labelled);
        if (parsed.IsFailure)
        {
            lines.Add(parsed.Error.Message);
            return new AnnouncementEvalReport(false, lines);
        }

        var manifestText = await files.ReadAsync(AnnouncementFile.Manifest, cancellationToken);
        var baselineText = await files.ReadAsync(AnnouncementFile.Baseline, cancellationToken);
        var baseline = baselineText is null ? null : AnnouncementCategoryBaseline.Parse(baselineText);
        var rows = parsed.Value;
        var byId = sample.Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        lines.Add($"labelled: {rows.Count} announcements, material {rows.Count(r => r.Material)}, non_material {rows.Count(r => !r.Material)}{(manifestText is null ? "; the manifest is missing" : string.Empty)}");
        foreach (var half in new[] { AnnouncementSampler.MaterialHalf, AnnouncementSampler.OtherHalf })
        {
            var inHalf = rows.Where(r => byId.TryGetValue(r.Id, out var row) && row.Half == half).ToList();
            lines.Add($"  half {half}: {inHalf.Count} rows, labelled material {inHalf.Count(r => r.Material)}, non_material {inHalf.Count(r => !r.Material)}");
        }

        if (baseline is { IsSuccess: true })
        {
            var predicted = rows.Where(r => baseline.Value.IsMaterial(r.Category)).ToList();
            var hits = predicted.Count(r => r.Material);
            // The weights are recomputed from the labelled rows per stratum (P15-08), so a reserve row that replaced an "unsure" one does not carry the weight of the drawn sample.
            var weights = AnnouncementWeights.Recompute(sample, rows);
            double Weight(LabelledAnnouncement r) => weights.GetValueOrDefault(r.Id, 1.0);
            var weightedPredicted = predicted.Sum(Weight);
            var weightedHits = predicted.Where(r => r.Material).Sum(Weight);
            lines.Add($"category baseline: predicts material for {predicted.Count} rows, {hits} of them labelled material (precision {Ratio(hits, predicted.Count)} on the sample); population-weighted (weights recomputed from the labelled rows per stratum) {(weightedPredicted > 0 ? (weightedHits / weightedPredicted).ToString("0.000", CultureInfo.InvariantCulture) : "n/a")}");
        }

        lines.Add("precision on this sample is a stratified estimate (half of the rows come from the baseline-material categories), not the precision on all announcements.");
        lines.Add(Formula);
        return new AnnouncementEvalReport(false, lines);
    }

    private static string Ratio(int k, int n) => n == 0 ? "n/a" : ((double)k / n).ToString("0.000", CultureInfo.InvariantCulture);
}
