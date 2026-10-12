namespace XDrishti.Domain.Market.Quality;

/// <summary>What started a quality run: the owner (<see cref="Manual"/>), the scheduled end-of-day run (<see cref="Daily"/>) or a data job the owner started that included the step (<see cref="AfterJob"/>). Stored by code.</summary>
public enum QualityTrigger
{
    Manual,
    Daily,
    AfterJob,
}

/// <summary>The codes of <see cref="QualityTrigger"/> in the database and the API: <c>manual</c> · <c>daily</c> · <c>after_job</c>.</summary>
public static class QualityTriggerCodes
{
    public const string Manual = "manual";
    public const string Daily = "daily";
    public const string AfterJob = "after_job";

    public static string ToCode(this QualityTrigger trigger) => trigger switch
    {
        QualityTrigger.Manual => Manual,
        QualityTrigger.Daily => Daily,
        QualityTrigger.AfterJob => AfterJob,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown quality trigger."),
    };

    public static QualityTrigger FromCode(string code) => code switch
    {
        Manual => QualityTrigger.Manual,
        Daily => QualityTrigger.Daily,
        AfterJob => QualityTrigger.AfterJob,
        _ => throw new ArgumentOutOfRangeException(nameof(code), code, "Unknown quality trigger."),
    };
}
