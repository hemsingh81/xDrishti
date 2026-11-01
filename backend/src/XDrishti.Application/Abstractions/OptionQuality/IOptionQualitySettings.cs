using XDrishti.Domain.Options.Quality;

namespace XDrishti.Application.Abstractions.OptionQuality;

/// <summary>The settings <c>Options:Quality:*</c> (P14-04): the numbers the checks judge by and the bounds of one run.</summary>
public interface IOptionQualitySettings
{
    OptionQualityThresholds Thresholds { get; }

    /// <summary>How many days back a day without a chain session row is reported as not recorded.</summary>
    int LookbackDays { get; }

    /// <summary>The most chain sessions one run checks (the rest wait for the next run).</summary>
    int MaxChainSessionsPerRun { get; }

    /// <summary>The most fetch-key windows one run checks.</summary>
    int MaxRollingWindowsPerRun { get; }

    /// <summary>The most minutes one run goes on before it stops between two units of work.</summary>
    int MaxRunMinutes { get; }
}
