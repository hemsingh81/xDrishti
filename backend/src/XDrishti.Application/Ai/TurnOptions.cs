using System.ComponentModel.DataAnnotations;

namespace XDrishti.Application.Ai;

/// <summary>Settings <c>Assistant:Turn</c> (spec P9-03). Bound and validated at start-up by the infrastructure registration; the application reads the values.</summary>
public sealed class TurnOptions
{
    public const string SectionName = "Assistant:Turn";

    [Range(100, 20_000)]
    public int MaxMessageChars { get; set; } = 2000;

    [Range(0, 20)]
    public int HistoryMessages { get; set; } = 6;

    [Range(64, 4000)]
    public int MaxAnswerTokens { get; set; } = 900;

    [Range(10, 600)]
    public int TurnTimeoutSeconds { get; set; } = 180;

    [Range(0.0, 2.0)]
    public double Temperature { get; set; } = 0.2;

    /// <summary>Sampling seed; null (empty) = none.</summary>
    public long? Seed { get; set; }

    /// <summary>The most characters of tool-result text sent to the model in one turn; later results are cut to <see cref="ToolResultCutChars"/>.</summary>
    [Range(1000, 100_000)]
    public int ToolResultTextBudget { get; set; } = 10_000;

    [Range(100, 10_000)]
    public int ToolResultCutChars { get; set; } = 1500;

    /// <summary>Seconds without an event after which the stream sends a ping.</summary>
    [Range(1, 60)]
    public int PingSeconds { get; set; } = 15;

    /// <summary>A turn is refused while the nightly service has been running for less than this many minutes (spec P9-00 D13).</summary>
    [Range(1, 600)]
    public int NightlyGuardMinutes { get; set; } = 60;

    /// <summary>The estimated tokens the prompt may take before the answer (system, tool schemas, history, question, results): the model's 8K prompt budget less the results reserve and the answer.</summary>
    [Range(1000, 30_000)]
    public int PromptBudgetTokens { get; set; } = 8000;
}
