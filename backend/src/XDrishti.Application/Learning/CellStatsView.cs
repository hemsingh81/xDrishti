using XDrishti.Application.Abstractions.Learning;

namespace XDrishti.Application.Learning;

/// <summary>One row of the cell statistics as the API shows it (P6-01), with the facts a reader needs to judge it (spec honest limits: the basis, the knobs, the evidence flag and the limits text).</summary>
public sealed record CellStatsView(
    string CellKey,
    string Strategy,
    string Timeframe,
    string Side,
    string Style,
    string Exit,
    string Regime,
    string Window,
    DateOnly AsOf,
    string Basis,
    int N,
    int Wins,
    double EffectiveN,
    double? WinRateRaw,
    double PriorRate,
    double WinRatePosterior,
    double WinRateLower,
    double WinRateUpper,
    double? AvgWinR,
    double? AvgLossR,
    double? ExpectancyR,
    double? ProfitFactor,
    double MaxDrawdownR,
    double? MaeP50,
    double? MaeP90,
    double? MfeP50,
    double? MfeP90,
    bool Enough,
    string Evidence,
    double RecencyHalfLifeDays,
    double PriorStrength,
    int MinTrades,
    double CredibleLevel,
    string SnapshotKey,
    DateTimeOffset CreatedAt,
    string Limits)
{
    /// <summary>Printed on every row (doc 09, P6-00 honest limits).</summary>
    public const string LimitsText = "Replayed history with modelled fills, about 2 years, in-sample on the data shown: a description of the past, not a forecast. The prior's parent group includes the cell's own trades. The credible interval treats trades as independent, which overlapping trades are not, so it is too narrow. One run per strategy feeds a snapshot, else signals would be counted twice.";

    internal static CellStatsView Of(StoredCellStats s)
    {
        var r = s.Row;
        return new CellStatsView(
            r.Cell.Key, r.Cell.Strategy, r.Cell.Timeframe, r.Cell.Side, r.Cell.Style, r.Cell.Exit, r.Cell.Regime, Domain.Learning.CellWindowExtensions.Code(r.Window), r.AsOf,
            r.Costed ? "net R (after charges)" : "gross R (before charges)",
            r.N, r.Wins, r.EffectiveN, r.WinRateRaw, r.PriorRate, r.WinRatePosterior, r.WinRateLower, r.WinRateUpper, r.AvgWinR, r.AvgLossR, r.ExpectancyR, r.ProfitFactor, r.MaxDrawdownR, r.MaeP50, r.MaeP90, r.MfeP50, r.MfeP90,
            r.Enough, r.Enough ? "enough" : "not enough evidence (fewer trades than the minimum)",
            s.Options.RecencyHalfLifeDays, s.Options.PriorStrength, s.Options.MinTrades, s.Options.CredibleLevel, s.SnapshotKey, s.CreatedAt, LimitsText);
    }
}
