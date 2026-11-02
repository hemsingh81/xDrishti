using System.Globalization;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Filings;

/// <summary>
/// <c>xd eval announcements sample</c> (spec P15-07 criterion 4): draws the 240 announcements (200 main, 40 reserve) from the stored, dated ones with <see cref="AnnouncementSampler"/> and writes <c>to-label.csv</c> (with text, <c>data/</c> only) and <c>sample.json</c> (no text). The
/// baseline file must exist and parse, and its hash goes into the sample. A sample that already exists is never replaced by a different one (the owner's labels would no longer match): the same sample is a no-op, another is refused <c>eval.sample_exists</c>. No model, no network.
/// </summary>
internal sealed class SampleAnnouncementsHandler(IAnnouncementCandidates candidates, IAnnouncementEvalFiles files) : ICommandHandler<SampleAnnouncementsCommand, AnnouncementEvalReport>
{
    private static readonly TimeSpan India = TimeSpan.FromMinutes(330);

    public async ValueTask<Result<AnnouncementEvalReport>> HandleAsync(SampleAnnouncementsCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.To < command.From)
        {
            return Fail("eval.bad_range", "--to is before --from.");
        }

        var baselineText = await files.ReadAsync(AnnouncementFile.Baseline, cancellationToken);
        if (baselineText is null)
        {
            return Fail("eval.baseline_missing", "eval/sets/announcement-baseline.json is not there; it is committed before the first sample.");
        }

        var baseline = AnnouncementCategoryBaseline.Parse(baselineText);
        if (baseline.IsFailure)
        {
            return Result.Failure<AnnouncementEvalReport>(baseline.Error);
        }

        var from = new DateTimeOffset(command.From.ToDateTime(TimeOnly.MinValue), India);
        var toExclusive = new DateTimeOffset(command.To.AddDays(1).ToDateTime(TimeOnly.MinValue), India);
        var found = await candidates.ListAsync(from, toExclusive, cancellationToken);
        var sampled = AnnouncementSampler.Sample(found.Dated, baseline.Value, command.Seed);
        if (sampled.IsFailure)
        {
            return Result.Failure<AnnouncementEvalReport>(sampled.Error);
        }

        var plan = sampled.Value;
        var bodies = await candidates.ReadBodiesAsync([.. plan.Rows.Select(r => r.Candidate.FilingId)], cancellationToken);
        var missing = plan.Rows.Count(r => !bodies.ContainsKey(r.Candidate.FilingId));
        if (missing > 0)
        {
            return Fail("eval.text_missing", $"The stored text of {missing} sampled announcements cannot be read; nothing was written.");
        }

        var rows = plan.Rows.Select(r => (Row: r, Body: bodies[r.Candidate.FilingId])).ToList();
        var csv = AnnouncementSampleCsv.Write(rows);
        var sample = new AnnouncementSampleFile(
            plan.Version, plan.Seed, command.From.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), command.To.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), baseline.Value.Sha256, found.Dated.Count, plan.EligibleMaterial, plan.EligibleOther, found.NoMetadata,
            AnnouncementSampler.Main, AnnouncementSampler.Reserve, plan.Strata,
            [.. rows.Select(r => new SampleFileRow(r.Row.Id, r.Row.Candidate.FilingId, r.Row.Half, r.Row.Reserve, r.Row.StratumEligible, r.Row.StratumSampled, r.Row.Weight,
                AnnouncementSampleCsv.RowHash(r.Row.Id, r.Row.Candidate.FilingId.ToString("D"), r.Row.Candidate.Symbol, AnnouncementSampleCsv.Time(r.Row.Candidate.PublishedAt), r.Body.Category, r.Body.Subject, r.Body.Text)))]);
        var json = sample.ToJson();

        var existingCsv = await files.ReadAsync(AnnouncementFile.ToLabel, cancellationToken);
        var existingJson = await files.ReadAsync(AnnouncementFile.Sample, cancellationToken);
        var changed = true;
        if (existingCsv is not null || existingJson is not null)
        {
            if (existingCsv != csv || existingJson != json)
            {
                return Fail("eval.sample_exists", "A sample already exists in data/eval/announcements/ and this one differs from it; labels made on the old one would no longer match. Move the folder away to start again.");
            }

            changed = false;
        }
        else
        {
            await files.WriteAsync(AnnouncementFile.ToLabel, csv, cancellationToken);
            await files.WriteAsync(AnnouncementFile.Sample, json, cancellationToken);
        }

        var main = plan.Rows.Count(r => !r.Reserve);
        return Result.Success(new AnnouncementEvalReport(changed,
        [
            changed ? "sampled: data/eval/announcements/to-label.csv and sample.json written" : "unchanged: the sample is already in data/eval/announcements/",
            $"{main} main rows (a001 to a{main:000}) and {plan.Rows.Count - main} reserve rows; eligible {found.Dated.Count} (material categories {plan.EligibleMaterial}, others {plan.EligibleOther}); announcement files without row metadata {found.NoMetadata} (never sampled)",
            $"seed {plan.Seed}, sampler {plan.Version}, baseline {baseline.Value.Sha256[..12]}, strata {plan.Strata.Count} ({plan.Strata.Count(s => s.Sampled == 0)} without a place)",
            "next: write material, non_material or unsure in the label column of to-label.csv (definition in eval/README.md), then xd eval announcements import <file>",
        ]));
    }

    private static Result<AnnouncementEvalReport> Fail(string code, string message) => Result.Failure<AnnouncementEvalReport>(Error.Validation(code, message));
}
