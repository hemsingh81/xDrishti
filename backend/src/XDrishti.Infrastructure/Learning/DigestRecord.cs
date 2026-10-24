namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.digests</c> (spec P7-15, ADR 0094): the weekly learning digest, one per ISO week. Insert-only for the runtime role.</summary>
internal sealed class DigestRecord
{
    public long Id { get; set; }

    public string IsoWeek { get; set; } = string.Empty;

    public DateOnly WeekStart { get; set; }

    public DateOnly WeekEnd { get; set; }

    public string Text { get; set; } = string.Empty;

    public string SectionsJson { get; set; } = string.Empty;

    public string BuiltBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
