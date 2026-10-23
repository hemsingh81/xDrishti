namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.lessons</c> (spec P6-16, ADR 0078). Insert-only; the opening row has no <see cref="LessonId"/> (its own id is the lesson id) and every later row repeats the facts with a new state.</summary>
internal sealed class LessonRecord
{
    public long Id { get; set; }

    public long? LessonId { get; set; }

    public string State { get; set; } = string.Empty;

    public string? FromState { get; set; }

    public string MonitorKind { get; set; } = string.Empty;

    public string Subject { get; set; } = string.Empty;

    public string EvidenceKey { get; set; } = string.Empty;

    public string Problem { get; set; } = string.Empty;

    public long? HypothesisRef { get; set; }

    public long? ExperimentId { get; set; }

    public string? Result { get; set; }

    public string? ExperimentOutcome { get; set; }

    public double? BeforeMeanR { get; set; }

    public double? AfterMeanR { get; set; }

    public double? BeforeTotalR { get; set; }

    public double? AfterTotalR { get; set; }

    public string Reason { get; set; } = string.Empty;

    public string Actor { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}
