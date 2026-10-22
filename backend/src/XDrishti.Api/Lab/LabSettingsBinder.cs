using XDrishti.Application.Lab;

namespace XDrishti.Api.Lab;

/// <summary>Reads the <c>StrategyLab</c> section (doc 16 <c>strategy_lab</c>) into <see cref="LabSettings"/>; a missing key keeps the default and a key out of range stops the host at start.</summary>
internal static class LabSettingsBinder
{
    public static LabSettings Bind(IConfiguration configuration)
    {
        var section = configuration.GetSection("StrategyLab");
        var d = LabSettings.Default;
        var settings = new LabSettings(
            section.GetValue("QuickYears", d.QuickYears),
            section.GetValue("QuickSymbols", d.QuickSymbols),
            section.GetValue("MaxParallelRuns", d.MaxParallelRuns),
            section.GetValue("CostSheetId", d.CostSheetId) ?? d.CostSheetId,
            TimeSpan.FromMilliseconds(section.GetValue("PollMilliseconds", (int)d.PollInterval.TotalMilliseconds)));
        if (settings.QuickYears is < 1 or > 5 || settings.QuickSymbols is < 1 or > 200 || settings.MaxParallelRuns is < 1 or > 8 || settings.PollInterval < TimeSpan.FromMilliseconds(50))
        {
            throw new InvalidOperationException("StrategyLab: QuickYears 1 to 5, QuickSymbols 1 to 200, MaxParallelRuns 1 to 8, PollMilliseconds 50 or more.");
        }

        return settings;
    }
}
