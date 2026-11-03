using XDrishti.Domain.Assistant.Evaluation;
using XDrishti.Domain.Filings;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Set D's labelled file as the eval found it (P15-07): absent (<see cref="Rows"/> and <see cref="Error"/> both null: the old state), valid (the rows) or invalid (the error names the line, never the text). The file is third-party text read as data. <see cref="Baseline"/> is the
/// pre-registered category baseline when its file is there and valid (P15-08 compares the model with it).
/// </summary>
internal sealed record AnnouncementSetLoad(IReadOnlyList<LabelledAnnouncement>? Rows, string? Error, AnnouncementCategoryBaseline? Baseline = null)
{
    public static AnnouncementSetLoad Of(string? jsonl, string? baselineJson = null)
    {
        var baseline = baselineJson is null ? null : AnnouncementCategoryBaseline.Parse(baselineJson) is { IsSuccess: true } parsedBaseline ? parsedBaseline.Value : null;
        if (jsonl is null)
        {
            return new AnnouncementSetLoad(null, null, baseline);
        }

        var parsed = AnnouncementSet.Parse(jsonl);
        return parsed.IsSuccess ? new AnnouncementSetLoad(parsed.Value, null, baseline) : new AnnouncementSetLoad(null, parsed.Error.Message, baseline);
    }

    /// <summary>
    /// What set D reports when it is not graded: no file keeps the old state; an invalid file fails the set with the line named; too few rows are not evaluable with the count named; enough rows without a valid baseline are not evaluable (never a pass). Null when the set is gradable.
    /// </summary>
    public AnnouncementResult? NotGraded(ThresholdTable thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return Error is not null ? AnnouncementGrader.InvalidFile(Error)
            : Rows is { } rows && rows.Count >= thresholds.EventsMinRows ? (Baseline is null ? AnnouncementGrader.NoBaseline(rows.Count) : null)
            : Rows is { } few ? AnnouncementGrader.TooFewRows(few.Count, thresholds.EventsMinRows)
            : AnnouncementGrader.Grade([], thresholds);
    }
}
