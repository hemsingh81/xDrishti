namespace XDrishti.Infrastructure.Learning;

/// <summary>A row of <c>research.model_versions</c> (spec P6-07, ADR 0070). It exists for the model and the migration; the registry writes and reads it with SQL. Insert-only.</summary>
internal sealed class ModelVersionRecord
{
    public long Id { get; set; }

    public string VersionKey { get; set; } = string.Empty;

    public string Sha256 { get; set; } = string.Empty;

    public int ArtifactBytes { get; set; }

    public string Trainer { get; set; } = string.Empty;

    public string? Note { get; set; }

    public string TrainingSetId { get; set; } = string.Empty;

    public string FeatureVersion { get; set; } = string.Empty;

    public int FeatureWidth { get; set; }

    public int FitRows { get; set; }

    public int CalibrationRows { get; set; }

    public int TrainRows { get; set; }

    public DateOnly FromDate { get; set; }

    public DateOnly ToDate { get; set; }

    public DateOnly? HoldOutStart { get; set; }

    public string Verdict { get; set; } = string.Empty;

    public int UsedFolds { get; set; }

    public int FoldsBeatingBoth { get; set; }

    public int FoldsNeeded { get; set; }

    public bool PooledBeatsBoth { get; set; }

    public double PooledEcePp { get; set; }

    public double PooledLogLoss { get; set; }

    public double PooledBrier { get; set; }

    public string Reasons { get; set; } = string.Empty;

    public string ReportText { get; set; } = string.Empty;

    public string Parameters { get; set; } = string.Empty;

    public double[] CalibrationScores { get; set; } = [];

    public double[] CalibrationValues { get; set; } = [];

    public double[] Importance { get; set; } = [];

    public DateTimeOffset CreatedAt { get; set; }

    public string CreatedBy { get; set; } = string.Empty;
}
