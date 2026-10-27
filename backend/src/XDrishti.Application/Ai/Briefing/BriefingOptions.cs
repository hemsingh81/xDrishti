using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Ai.Briefing;

/// <summary>Settings <c>Assistant:Briefing</c> (spec P9-06). Config only: no endpoint changes them. <see cref="UseModel"/> is OFF by default, so no briefing text is written by a model until the owner turns it on.</summary>
public sealed class BriefingOptions
{
    public const string SectionName = "Assistant:Briefing";

    /// <summary>Whether the model may phrase the text at all (it also needs <c>ai.settings.enabled</c>).</summary>
    public bool UseModel { get; set; }

    [Range(1, 2)]
    public int MaxAttempts { get; set; } = 2;

    public long Seed { get; set; } = 1337;

    [Range(50, 1000)]
    public int DailyMaxWords { get; set; } = 300;

    [Range(50, 1000)]
    public int WeeklyMaxWords { get; set; } = 400;

    /// <summary>India time before which a daily briefing starts no model call (spec P9-00 D13: the model stays out of the nightly window).</summary>
    [RegularExpression(@"^([01]\d|2[0-3]):[0-5]\d$")]
    public string EarliestIst { get; set; } = "20:00";

    [Range(1000, 8000)]
    public int MaxPromptTokens { get; set; } = 6000;

    /// <summary>How long a run waits for the nightly run, a chat turn or the time of day before it gives up without storing anything. 0 = do not wait.</summary>
    [Range(0, 200)]
    public int GateWaitMinutes { get; set; } = 180;

    /// <summary>How long a run waits for the model to be ready (copied from <c>Assistant:Llm:ReadyWaitSeconds</c> at start-up).</summary>
    [Range(0, 900)]
    public int ReadyWaitSeconds { get; set; } = 300;

    /// <summary>The earliest time as a value.</summary>
    public TimeOnly EarliestTime => TimeOnly.TryParseExact(EarliestIst, "HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var time) ? time : new TimeOnly(20, 0);
}
