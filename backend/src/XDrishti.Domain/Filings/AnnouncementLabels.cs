using XDrishti.Domain.Common;

namespace XDrishti.Domain.Filings;

/// <summary>
/// The import checks of the labelling file (P15-07), pure. Every id must be from the sample and appear once; the text columns must hash as they did at the sample; a label is <c>material</c>, <c>non_material</c> or <c>unsure</c> (case ignored). Each main row (<c>a001</c> to
/// <c>a200</c>) is blank-free; each <c>unsure</c> is replaced by the next reserve row in order, which must then be labelled definitely (an <c>unsure</c> reserve is passed over). Fewer than 200 definite labels is refused. The first problem is returned with the row named; the
/// text of a cell is never put in a message.
/// </summary>
public static class AnnouncementLabels
{
    public const string Unsure = "unsure";

    public static Result<IReadOnlyList<LabelledPick>> Resolve(AnnouncementSampleFile sample, IReadOnlyList<LabelLine> lines)
    {
        ArgumentNullException.ThrowIfNull(sample);
        ArgumentNullException.ThrowIfNull(lines);
        var byId = sample.Rows.ToDictionary(r => r.Id, StringComparer.Ordinal);
        var given = new Dictionary<string, LabelLine>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (!byId.TryGetValue(line.Id, out var row))
            {
                return Refuse("eval.unknown_id", $"line {line.Line}: the id is not from the sample");
            }

            if (!given.TryAdd(line.Id, line))
            {
                return Refuse("eval.duplicate_id", $"line {line.Line}: {line.Id} appears twice");
            }

            var hash = AnnouncementSampleCsv.RowHash(line.Id, line.FilingId, line.Symbol, line.PublishedAt, line.Category, line.Subject, line.Text);
            if (!string.Equals(hash, row.RowSha256, StringComparison.Ordinal))
            {
                return Refuse("eval.text_changed", $"line {line.Line}: {line.Id} has a text column that is not the one that was sampled");
            }

            var label = line.Label.ToLowerInvariant();
            if (label.Length > 0 && label is not (AnnouncementCategoryBaseline.Material or AnnouncementCategoryBaseline.NonMaterial or Unsure))
            {
                return Refuse("eval.bad_label", $"line {line.Line}: {line.Id} has a label that is not material, non_material or unsure");
            }
        }

        var main = sample.Rows.Where(r => !r.Reserve).ToList();
        var reserve = new Queue<SampleFileRow>(sample.Rows.Where(r => r.Reserve));
        var picks = new List<LabelledPick>();
        foreach (var row in main)
        {
            if (!given.TryGetValue(row.Id, out var line))
            {
                return Refuse("eval.missing_row", $"{row.Id} is not in the file");
            }

            var label = line.Label.ToLowerInvariant();
            if (label.Length == 0)
            {
                return Refuse("eval.bad_label", $"line {line.Line}: {row.Id} has no label");
            }

            if (label != Unsure)
            {
                picks.Add(new LabelledPick(row, label));
                continue;
            }

            LabelledPick? replacement = null;
            while (replacement is null && reserve.TryDequeue(out var next))
            {
                if (!given.TryGetValue(next.Id, out var reserveLine))
                {
                    return Refuse("eval.missing_row", $"{next.Id} (a reserve row) is not in the file");
                }

                var reserveLabel = reserveLine.Label.ToLowerInvariant();
                if (reserveLabel.Length == 0)
                {
                    return Refuse("eval.bad_label", $"line {reserveLine.Line}: {next.Id} replaces {row.Id} and has no label");
                }

                replacement = reserveLabel == Unsure ? null : new LabelledPick(next, reserveLabel);
            }

            if (replacement is null)
            {
                return Refuse("eval.too_few_labels", $"{row.Id} is unsure and no reserve row is left; {AnnouncementSampler.Main} definite labels are needed");
            }

            picks.Add(replacement);
        }

        return Result.Success<IReadOnlyList<LabelledPick>>(picks);
    }

    private static Result<IReadOnlyList<LabelledPick>> Refuse(string code, string message) => Result.Failure<IReadOnlyList<LabelledPick>>(Error.Validation(code, message));
}
