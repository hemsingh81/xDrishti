using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>xd eval announcements import</c> (spec P15-07 criterion 5): checks the owner's labelled file against <c>sample.json</c> (every id from the sample once, the text columns unchanged by hash, labels in the three words, <c>unsure</c> replaced by the next reserve row,
/// 200 definite labels) and that the baseline file is unchanged since the sample; then writes <c>data/eval/sets/announcements.jsonl</c> (the P9-09 schema, <c>source</c> <c>nse-announcement</c>) and the committed manifest (ids and hashes, no text). Importing the same file again
/// writes nothing. A refusal names the row and never repeats a cell. No model, no network.
/// </summary>
internal sealed class ImportAnnouncementLabelsHandler(IAnnouncementEvalFiles files) : ICommandHandler<ImportAnnouncementLabelsCommand, AnnouncementEvalReport>
{
    public const string Source = "nse-announcement";

    public async ValueTask<Result<AnnouncementEvalReport>> HandleAsync(ImportAnnouncementLabelsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        var sampleText = await files.ReadAsync(AnnouncementFile.Sample, cancellationToken);
        var sample = sampleText is null ? null : AnnouncementSampleFile.Parse(sampleText);
        if (sample is null)
        {
            return Fail("eval.no_sample", "There is no valid sample in data/eval/announcements/; run xd eval announcements sample first.");
        }

        var baselineText = await files.ReadAsync(AnnouncementFile.Baseline, cancellationToken);
        if (baselineText is null || AnnouncementCategoryBaseline.HashOf(baselineText) != sample.BaselineSha256)
        {
            return Fail("eval.baseline_changed", "eval/sets/announcement-baseline.json is not the file the sample was drawn with; the baseline is fixed before the labels.");
        }

        var lines = AnnouncementSampleCsv.Read(command.LabelFileText);
        if (lines is null)
        {
            return Fail("eval.bad_file", "The label file is not the exported csv (header id,filing_id,symbol,published_at,category,subject,text,label, one record per row).");
        }

        var resolved = AnnouncementLabels.Resolve(sample, lines);
        if (resolved.IsFailure)
        {
            return Result.Failure<AnnouncementEvalReport>(resolved.Error);
        }

        var byId = lines.ToDictionary(l => l.Id, StringComparer.Ordinal);
        var rows = resolved.Value.Select(p => ToRow(p, byId[p.Row.Id])).ToList();
        var jsonl = AnnouncementSet.Write(rows);
        var manifest = AnnouncementManifest.Build(sample, AnnouncementCategoryBaseline.HashOf(command.LabelFileText), resolved.Value).ToJson();

        var unchanged = await files.ReadAsync(AnnouncementFile.Labelled, cancellationToken) == jsonl && await files.ReadAsync(AnnouncementFile.Manifest, cancellationToken) == manifest;
        if (!unchanged)
        {
            await files.WriteAsync(AnnouncementFile.Labelled, jsonl, cancellationToken);
            await files.WriteAsync(AnnouncementFile.Manifest, manifest, cancellationToken);
        }

        var material = rows.Count(r => r.Material);
        var replaced = resolved.Value.Count(p => p.Row.Reserve);
        return Result.Success(new AnnouncementEvalReport(!unchanged,
        [
            unchanged ? "unchanged: the labelled set is already as this file makes it" : "imported: data/eval/sets/announcements.jsonl and eval/sets/announcements.manifest.json written",
            $"{rows.Count} labelled announcements: material {material}, non_material {rows.Count - material}; {replaced} replaced by reserve rows",
            "the manifest (ids and hashes, no text) is for git; the labelled text stays in data/",
        ]));
    }

    private static LabelledAnnouncement ToRow(LabelledPick pick, LabelLine line) =>
        new(pick.Row.Id, line.Symbol, line.PublishedAt, Source, AnnouncementSampleCsv.NormalText(line.Text), pick.Label == AnnouncementCategoryBaseline.Material, line.Category);

    private static Result<AnnouncementEvalReport> Fail(string code, string message) => Result.Failure<AnnouncementEvalReport>(Error.Validation(code, message));
}
