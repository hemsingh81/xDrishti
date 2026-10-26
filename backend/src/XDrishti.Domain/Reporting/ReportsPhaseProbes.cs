using XDrishti.Domain.Common;

namespace XDrishti.Domain.Reporting;

/// <summary>The pure probes of the no-double-counting rule (P8-00 criterion 4): the engine is asked to build the two sets it must refuse. No I/O.</summary>
public static class ReportsPhaseProbes
{
    private const string Universe = "basket:probe";

    private static readonly DateOnly Day = new(2025, 1, 6);

    /// <summary>Two runs of one strategy over the same universe and window, holding the same trade, are refused with <c>reports.run_overlap</c>.</summary>
    public static bool OverlapRefused()
    {
        var runs = new List<ReportRun> { Run("a", costed: false, Universe), Run("b", costed: false, Universe) };
        return Refused(runs, [Trade("a", costed: false), Trade("b", costed: false)], "reports.run_overlap");
    }

    /// <summary>A gross and a costed run in one set are refused with <c>reports.mixed_basis</c>.</summary>
    public static bool MixedBasisRefused()
    {
        var runs = new List<ReportRun> { Run("a", costed: false, Universe), Run("b", costed: true, "basket:other") };
        return Refused(runs, [Trade("a", costed: false), Trade("b", costed: true)], "reports.mixed_basis");
    }

    private static bool Refused(List<ReportRun> runs, List<ReportTrade> trades, string code)
    {
        var result = ReportPopulation.Create(runs, trades, new ReportOptions());
        return result.IsFailure && result.Error.Code == code;
    }

    private static ReportRun Run(string key, bool costed, string universe) => new(key, universe, Day, Day.AddDays(30), "intraday", "FIXED_RR", costed);

    private static ReportTrade Trade(string run, bool costed) =>
        new(run, "t1", "ORB", "15m", "BUY", Guid.Empty, "XYZ", new ValueList<string>([]), Day, Day.AddDays(1), 1.0, costed ? 0.9 : null, 1);
}
