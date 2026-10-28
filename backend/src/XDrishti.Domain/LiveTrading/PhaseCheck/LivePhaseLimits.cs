namespace XDrishti.Domain.LiveTrading;

/// <summary>The floors of the check and the limits printed with every report (spec P11-06 criterion 9). The floors are options of the code, printed with the report; a changed value is visible in the log of the options.</summary>
public static class LivePhaseLimits
{
    public const int MinDays = 56;
    public const int MinSessions = 40;
    public const int MinRoundTrips = 40;
    public const int MinPairs = 30;
    public const double MinCoverage = 0.90;
    public const int MinRiskSessions = 20;
    public const double ScaleMin = 0.125;
    public const double ScaleMax = 0.5;
    public const int MinSlippageN = 20;
    public const double SlippageUpperR = 0.10;
    public const int StepDownSessions = 20;
    public const double MaxEvidenceMissingShare = 0.20;
    public const int EstimateWeeks = 4;
    public const int UnknownGraceSessions = 5;

    public static IReadOnlyList<string> Limits { get; } =
    [
        "About 40 to 80 round trips in eight weeks give an interval of roughly +-0.05 R on a paired sd of 0.15; a narrower claim is not available.",
        "One market regime: a MET says nothing about another.",
        "Simulated paper fills are a model, so live minus paper is live minus a model, not live minus a market.",
        "Arrival prices come from 1-minute bars.",
        "The owner's discretionary trades are reported apart and are not in the pairs.",
        "Dhan field names are unverified until the recorded fixture; the swing stop protection is unknown.",
        "The static IP and SEBI facts are the owner's notes (P11-05), not checked at Dhan or the ISP.",
        "No order placed by the system, no look-ahead in the grading replay, determinism and paper-equals-grading are not judged here: tests prove them (the architecture tripwire, the allow-list pin, P3-08, P10-01).",
        "The report reads stored records and the options of the process that ran it; nothing is written.",
    ];
}
