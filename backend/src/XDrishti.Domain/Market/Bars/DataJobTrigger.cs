namespace XDrishti.Domain.Market.Bars;

/// <summary>What started a data job (ADR 0017 §2): the owner (<see cref="Manual"/>) or the scheduled end-of-day run (<see cref="Daily"/>). Stored by name — never rename a value.</summary>
public enum DataJobTrigger
{
    Manual,
    Daily,
}

/// <summary>The codes of <see cref="DataJobTrigger"/> used in the database and the API: <c>manual</c> · <c>daily</c>.</summary>
public static class DataJobTriggerCodes
{
    public const string Manual = "manual";
    public const string Daily = "daily";

    public static string ToCode(this DataJobTrigger trigger) => trigger switch
    {
        DataJobTrigger.Manual => Manual,
        DataJobTrigger.Daily => Daily,
        _ => throw new ArgumentOutOfRangeException(nameof(trigger), trigger, "Unknown data job trigger."),
    };
}
