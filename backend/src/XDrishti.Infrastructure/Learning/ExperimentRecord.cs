namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.experiments</c> (spec P6-11, ADR 0072). It exists for the model and the migration; the ledger writes and reads it with SQL. Insert-only.</summary>
internal sealed class ExperimentRecord
{
    public long Id { get; set; }

    public string ExperimentKey { get; set; } = string.Empty;

    public string KnobName { get; set; } = string.Empty;

    public string KnobKind { get; set; } = string.Empty;

    public string BaselineValue { get; set; } = string.Empty;

    public string CandidateValue { get; set; } = string.Empty;

    public string BaselineRunKey { get; set; } = string.Empty;

    public string CandidateRunKey { get; set; } = string.Empty;

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public DateOnly? HoldOutStart { get; set; }

    public string Outcome { get; set; } = string.Empty;

    public int Trials { get; set; }

    public int UsedFolds { get; set; }

    public int FoldsBetter { get; set; }

    public int FoldsNeeded { get; set; }

    public double RequiredImprovementR { get; set; }

    public int BaselineTrades { get; set; }

    public int CandidateTrades { get; set; }

    public double? PooledBaselineMeanR { get; set; }

    public double? PooledCandidateMeanR { get; set; }

    public double BaselineTotalR { get; set; }

    public double CandidateTotalR { get; set; }

    public double ExpectedLiveGapR { get; set; }

    public long CompareMs { get; set; }

    public string Reasons { get; set; } = string.Empty;

    public string ReportText { get; set; } = string.Empty;

    public string Parameters { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
