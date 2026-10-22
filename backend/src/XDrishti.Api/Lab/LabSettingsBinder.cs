using XDrishti.Application.Lab;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Research;

namespace XDrishti.Api.Lab;

/// <summary>Reads the <c>StrategyLab</c> section (doc 16 <c>strategy_lab</c>) into <see cref="LabSettings"/>; a missing key keeps the default and a key out of range stops the host at start.</summary>
internal static class LabSettingsBinder
{
    public static LabSettings Bind(IConfiguration configuration)
    {
        var section = configuration.GetSection("StrategyLab");
        var gates = section.GetSection("Gates");
        var d = LabSettings.Default;
        var settings = new LabSettings(
            section.GetValue("QuickYears", d.QuickYears),
            section.GetValue("QuickSymbols", d.QuickSymbols),
            section.GetValue("MaxParallelRuns", d.MaxParallelRuns),
            section.GetValue("CostSheetId", d.CostSheetId) ?? d.CostSheetId,
            TimeSpan.FromMilliseconds(section.GetValue("PollMilliseconds", (int)d.PollInterval.TotalMilliseconds)),
            new QualificationGates(
                gates.GetValue("MinOosTrades", d.Gates.MinOosTrades),
                gates.GetValue("MinExpectancyR", d.Gates.MinExpectancyR),
                gates.GetValue("MinProfitFactor", d.Gates.MinProfitFactor),
                gates.GetValue("MinPositiveFoldsPct", d.Gates.MinPositiveFoldsPct),
                gates.GetValue("MaxDrawdownR", d.Gates.MaxDrawdownR),
                gates.GetValue("MaxCalibrationErrorPp", d.Gates.MaxCalibrationErrorPp)),
            section.GetValue("MinFoldsForPass", d.MinFoldsForPass),
            section.GetValue("MultipleTesting", d.MultipleTesting),
            section.GetSection("FullExits").Get<string[]>());
        if (settings.QuickYears is < 1 or > 5 || settings.QuickSymbols is < 1 or > 200 || settings.MaxParallelRuns is < 1 or > 8 || settings.PollInterval < TimeSpan.FromMilliseconds(50) || settings.MinFoldsForPass is < 1 or > 100 || !GatesValid(settings.Gates))
        {
            throw new InvalidOperationException("StrategyLab: QuickYears 1 to 5, QuickSymbols 1 to 200, MaxParallelRuns 1 to 8, PollMilliseconds 50 or more, MinFoldsForPass 1 to 100, Gates not negative (MinPositiveFoldsPct up to 100).");
        }

        var errors = new List<string>();
        if (ExitFlags.Parse([.. settings.Exits], null, errors) is not { Candidates.Count: >= 2 } || errors.Count > 0)
        {
            throw new InvalidOperationException("StrategyLab:FullExits must be --exit flags that expand to two to 16 candidates.");
        }

        return settings;
    }

    private static bool GatesValid(QualificationGates g) =>
        g.MinOosTrades >= 0 && double.IsFinite(g.MinExpectancyR) && g.MinExpectancyR >= 0 && g.MinProfitFactor >= 1 && g.MinPositiveFoldsPct is >= 0 and <= 100 && g.MaxDrawdownR >= 0 && g.MaxCalibrationErrorPp >= 0;
}
