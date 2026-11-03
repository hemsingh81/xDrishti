using System.Diagnostics;
using Microsoft.Extensions.Logging;
using XDrishti.Application.Abstractions.Ai;
using XDrishti.Application.Abstractions.Filings;
using XDrishti.Application.Ai;
using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Common;
using XDrishti.Domain.Filings;
using XDrishti.Domain.Filings.Extraction;
using AnnouncementRow = XDrishti.Domain.Assistant.Evaluation.AnnouncementRow;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Set D's classifier run (spec P15-08 criterion 6): each labelled row goes through the same <see cref="IAnnouncementFlagger"/> as the stored extractions, <c>repeats</c> times, one request at a time and under the turn gate. A case's prediction counts as material only if every repeat said
/// material and not-material only if every repeat said so; repeats that disagree are <c>unstable</c>, counted as not material and reported. An answer that is not <c>ok</c> (invalid output, too long) is not a material prediction. The category baseline's class and the inclusion
/// weights recomputed from the labelled rows (when <c>sample.json</c> is there) go into the grade. The fixed hostile set is run once through the flagger and its states are counted in the report. A model failure stops the run (never a partial grade). Nothing is stored here: the labelled
/// text is data, read once, and the stored run keeps only counts.
/// </summary>
internal sealed class AnnouncementSetRunner(IAnnouncementFlagger flagger, IAnnouncementEvalFiles files, TurnGate gate, ILogger<AnnouncementSetRunner> logger)
{
    public async ValueTask<Result<AnnouncementResult>> RunAsync(IReadOnlyList<LabelledAnnouncement> labelled, AnnouncementCategoryBaseline baseline, ThresholdTable thresholds, int repeats, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(labelled);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(thresholds);
        var weights = await WeightsAsync(labelled, cancellationToken);
        var rows = new List<AnnouncementRow>(labelled.Count);
        foreach (var item in labelled)
        {
            var clock = Stopwatch.StartNew();
            var said = new List<bool>(repeats);
            for (var repeat = 1; repeat <= repeats; repeat++)
            {
                var asked = await AskAsync(new AnnouncementFlagInput(string.Empty, item.Category ?? string.Empty, item.Text), cancellationToken);
                if (asked.IsFailure)
                {
                    return Result.Failure<AnnouncementResult>(asked.Error);
                }

                said.Add(asked.Value.State == ExtractionStates.Ok && asked.Value.Answer!.Material);
            }

            var unstable = said.Distinct().Count() > 1;
            var predicted = !unstable && said[0];
            rows.Add(new AnnouncementRow(item.Id, item.Material, predicted, baseline.IsMaterial(item.Category), weights.GetValueOrDefault(item.Id), unstable));
            EvalLog.CaseDone(logger, "events", item.Id, repeats, predicted == item.Material, (int)clock.ElapsedMilliseconds);
        }

        var graded = AnnouncementGrader.Grade(rows, thresholds);
        var hostile = await HostileAsync(cancellationToken);
        return hostile.IsFailure ? Result.Failure<AnnouncementResult>(hostile.Error) : Result.Success(graded with { Detail = graded.Detail is null ? null : graded.Detail with { HostileSet = hostile.Value } });
    }

    private async ValueTask<Result<AnnouncementFlag>> AskAsync(AnnouncementFlagInput input, CancellationToken cancellationToken)
    {
        using var slot = gate.TryEnter();
        if (slot is null)
        {
            return Result.Failure<AnnouncementFlag>(EvalErrors.Busy());
        }

        try
        {
            return Result.Success(await flagger.FlagAsync(input, cancellationToken));
        }
        catch (ModelUnavailableException)
        {
            return Result.Failure<AnnouncementFlag>(EvalErrors.ModelUnavailable());
        }
    }

    private async ValueTask<Result<string>> HostileAsync(CancellationToken cancellationToken)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var hostile in HostileAnnouncements.All)
        {
            var asked = await AskAsync(new AnnouncementFlagInput(hostile.Subject, "other", hostile.Text), cancellationToken);
            if (asked.IsFailure)
            {
                return Result.Failure<string>(asked.Error);
            }

            counts[asked.Value.State] = counts.GetValueOrDefault(asked.Value.State) + 1;
        }

        return Result.Success($"hostile_set: {HostileAnnouncements.All.Count} injected announcements, " + string.Join(", ", ExtractionStates.All.Select(s => $"{s} {counts.GetValueOrDefault(s)}")) + "; each is a schema-valid answer or a closed refusal, never a tool call");
    }

    private async ValueTask<IReadOnlyDictionary<string, double>> WeightsAsync(IReadOnlyList<LabelledAnnouncement> labelled, CancellationToken cancellationToken)
    {
        var text = await files.ReadAsync(AnnouncementFile.Sample, cancellationToken);
        var sample = text is null ? null : AnnouncementSampleFile.Parse(text);
        return sample is null ? new Dictionary<string, double>() : AnnouncementWeights.Recompute(sample, labelled);
    }
}
