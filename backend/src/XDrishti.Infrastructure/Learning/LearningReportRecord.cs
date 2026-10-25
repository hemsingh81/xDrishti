namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.learning_reports</c> (spec P8-12, ADR 0110): a generic, kind-keyed learning report. Insert-only for the runtime role.</summary>
internal sealed class LearningReportRecord
{
    public long Id { get; set; }

    public string Kind { get; set; } = string.Empty;

    public string InputHash { get; set; } = string.Empty;

    public string Outcome { get; set; } = string.Empty;

    public DateOnly AsOf { get; set; }

    public string Basis { get; set; } = string.Empty;

    public string SourcesJson { get; set; } = string.Empty;

    public string OptionsJson { get; set; } = string.Empty;

    public string ReportJson { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public string BuiltBy { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
