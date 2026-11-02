using XDrishti.Domain.Assistant.Evaluation;

namespace XDrishti.Application.Assistant.Evaluation;

/// <summary>
/// Set D's labelled file as the eval found it (P15-07): absent (<see cref="Rows"/> and <see cref="Error"/> both null: the old state), valid (the rows) or invalid (the error names the line, never the text). The file is third-party text read as data.
/// </summary>
internal sealed record AnnouncementSetLoad(IReadOnlyList<LabelledAnnouncement>? Rows, string? Error)
{
    public static AnnouncementSetLoad Of(string? jsonl)
    {
        if (jsonl is null)
        {
            return new AnnouncementSetLoad(null, null);
        }

        var parsed = AnnouncementSet.Parse(jsonl);
        return parsed.IsSuccess ? new AnnouncementSetLoad(parsed.Value, null) : new AnnouncementSetLoad(null, parsed.Error.Message);
    }

    /// <summary>What set D reports today: no classifier exists before P15-08, so a valid file is not evaluable for that reason; no file keeps the old state; an invalid file fails the set with the line named.</summary>
    public AnnouncementResult Result(ThresholdTable thresholds)
    {
        ArgumentNullException.ThrowIfNull(thresholds);
        return Error is not null ? AnnouncementGrader.InvalidFile(Error)
            : Rows is { } rows && rows.Count >= thresholds.EventsMinRows ? AnnouncementGrader.NoClassifierYet(rows.Count)
            : Rows is { } few ? AnnouncementGrader.TooFewRows(few.Count, thresholds.EventsMinRows)
            : AnnouncementGrader.Grade([], thresholds);
    }
}
