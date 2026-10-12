namespace XDrishti.Domain.Market.Quality;

/// <summary>Where a quality run stands. Stored by code (lower case) — never rename a value.</summary>
public enum QualityRunStatus
{
    Running,
    Succeeded,

    /// <summary>The run crashed or was interrupted (the application stopped); its results so far are kept.</summary>
    Failed,
}
