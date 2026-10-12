namespace XDrishti.Domain.Market.Bars;

/// <summary>Where a data job stands. Stored by name — never rename a value.</summary>
public enum DataJobStatus
{
    Running,

    /// <summary>Every phase that was asked for ran or was skipped for a reason that is not an error.</summary>
    Succeeded,

    /// <summary>A phase failed, or the job crashed or was interrupted.</summary>
    Failed,
}
