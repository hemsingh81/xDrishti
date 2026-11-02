namespace XDrishti.Application.Abstractions.Filings;

/// <summary>The files of the announcement eval (P15-07), each at a fixed place: the committed baseline and manifest under <c>eval/sets/</c> (the baseline is read only), and the labelled text under <c>data/eval/</c> (git-ignored).</summary>
public enum AnnouncementFile
{
    /// <summary><c>eval/sets/announcement-baseline.json</c>, committed before the first sample. Never written by the app.</summary>
    Baseline,

    /// <summary><c>data/eval/announcements/to-label.csv</c>: the sample with its text and an empty label column. Third-party text.</summary>
    ToLabel,

    /// <summary><c>data/eval/announcements/sample.json</c>: the sample's seed, strata, weights and row hashes. No text.</summary>
    Sample,

    /// <summary><c>data/eval/sets/announcements.jsonl</c>: the labelled set of the P9-09 schema. Third-party text.</summary>
    Labelled,

    /// <summary><c>eval/sets/announcements.manifest.json</c>, committed: ids and hashes, no text.</summary>
    Manifest,
}

/// <summary>Reads and writes those files. A write goes to a temporary name and is moved into place; folders are created 0700 and files 0600; nothing is ever deleted.</summary>
public interface IAnnouncementEvalFiles
{
    /// <summary>The text of the file, or null when it is not there.</summary>
    ValueTask<string?> ReadAsync(AnnouncementFile file, CancellationToken cancellationToken);

    ValueTask WriteAsync(AnnouncementFile file, string text, CancellationToken cancellationToken);
}
