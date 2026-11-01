using Microsoft.Extensions.Options;
using XDrishti.Application.Abstractions.OptionQuality;
using XDrishti.Domain.Options.Quality;

namespace XDrishti.Infrastructure.OptionQuality;

internal sealed class OptionQualitySettings(IOptions<OptionQualityOptions> options) : IOptionQualitySettings
{
    public OptionQualityThresholds Thresholds { get; } = new()
    {
        Rate = options.Value.Rate,
        MaxIv = options.Value.MaxIv,
        OiJumpPct = options.Value.OiJumpPct,
        CoveragePass = options.Value.CoveragePass,
        CoverageWarn = options.Value.CoverageWarn,
        ParityPass = options.Value.ParityPass,
        ParityWarn = options.Value.ParityWarn,
        GreeksWarn = options.Value.GreeksWarn,
        GreeksFail = options.Value.GreeksFail,
        OiWarnShare = options.Value.OiWarnShare,
        OiJumpMinLots = options.Value.OiJumpMinLots,
        TickPaise = options.Value.TickPaise,
    };

    public int LookbackDays => options.Value.LookbackDays;

    public int MaxChainSessionsPerRun => options.Value.MaxChainSessionsPerRun;

    public int MaxRollingWindowsPerRun => options.Value.MaxRollingWindowsPerRun;

    public int MaxRunMinutes => options.Value.MaxRunMinutes;
}
