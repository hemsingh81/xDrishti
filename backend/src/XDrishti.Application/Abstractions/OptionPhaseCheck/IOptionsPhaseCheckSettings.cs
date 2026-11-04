namespace XDrishti.Application.Abstractions.OptionPhaseCheck;

/// <summary>The setting <c>Options:PhaseCheck:*</c> (P14-17): after how many days an archive watermark makes a stored verdict stale.</summary>
public interface IOptionsPhaseCheckSettings
{
    /// <summary>7 to 730, default 90.</summary>
    int StaleAfterDays { get; }
}
