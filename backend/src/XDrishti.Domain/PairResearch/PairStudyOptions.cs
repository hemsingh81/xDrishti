using System.Globalization;
using XDrishti.Domain.Lab;
using XDrishti.Domain.Pairs;

namespace XDrishti.Domain.PairResearch;

/// <summary>
/// The pre-registered settings of the pairs study (P14-13, protocol S). A different value is a different trial; the canonical text of <see cref="Canonical"/> is what the ledger and the records hash. <see cref="PairNotional"/> is a run setting (it does not change a trial).
/// The Lab gates are the Lab's own defaults (<see cref="QualificationGates.Default"/>: 30 trades, 0.15 R, profit factor 1.2, 60 % positive folds, 10 R drawdown, 5 pp calibration): no threshold is invented here. The Lab scales its trade, expectancy and profit-factor minimums by the factor of N, so at N = 2,006 it asks for more than the protocol's own 300 trades
/// and 0.05 R; the protocol's rules are separate. Whether the Lab gates are the right bar for pairs is an owner question (ADR 0196). The calibration gate does not apply (no probability) and is reported as not applicable.
/// </summary>
public sealed record PairStudyOptions
{
    public static PairStudyOptions Default { get; } = new();

    /// <summary>The core settings: lags, q, correlation, half-life range, exit 0.5, stop 3.5 (the entry is the variant's).</summary>
    public PairsOptions Core { get; init; } = PairsOptions.Default;

    /// <summary>Daily closes of a formation window.</summary>
    public int FormationSessions { get; init; } = 252;

    /// <summary>The most candidate pairs a formation keeps.</summary>
    public int MaxCandidates { get; init; } = 2000;

    /// <summary>Y-leg notional in rupees.</summary>
    public decimal PairNotional { get; init; } = 100_000m;

    /// <summary>First minute of the session (09:15) as a minute of the day.</summary>
    public int OpenMinute { get; init; } = 555;

    /// <summary>The latest decision minute that may open a pair (14:45).</summary>
    public int LastEntryMinute { get; init; } = 885;

    /// <summary>The decision minute at which an open pair is closed (15:15).</summary>
    public int SquareOffMinute { get; init; } = 915;

    /// <summary>Minutes in a spread bar.</summary>
    public int BarMinutes { get; init; } = 5;

    public int MinTradesPerQuarter { get; init; } = 20;

    public int MinQuarters { get; init; } = 6;

    public int MinTrades { get; init; } = 300;

    /// <summary>The smallest mean R worth having, widened by the factor.</summary>
    public double MarginR { get; init; } = 0.05;

    /// <summary>The share of the net profit one pair may make before the result is not a KEEP.</summary>
    public double MaxPairShare { get; init; } = 0.25;

    public double PositiveQuarterShare { get; init; } = 2.0 / 3.0;

    public int Draws { get; init; } = 10_000;

    public ulong Seed { get; init; } = 1337;

    public double Level { get; init; } = 0.90;

    /// <summary>The longer block of the sensitivity, in sessions.</summary>
    public int BlockSessions { get; init; } = 5;

    public int HoldOutMonths { get; init; } = 6;

    public int MinHoldOutTrades { get; init; } = 100;

    public int EmbargoSessions { get; init; } = 5;

    /// <summary>The gates the Lab's verdict function judges the pooled trades on.</summary>
    public QualificationGates Gates { get; init; } = QualificationGates.Default;

    /// <summary>True when every setting can be used.</summary>
    public bool IsValid =>
        Core.IsValid && FormationSessions >= Core.MinObservations && MaxCandidates is >= 1 and <= 100_000 && PairNotional > 0
        && OpenMinute is >= 0 and < 1440 && OpenMinute < LastEntryMinute && LastEntryMinute <= SquareOffMinute && SquareOffMinute < 1440 && BarMinutes is >= 1 and <= 60
        && MinTradesPerQuarter >= 1 && MinQuarters >= 1 && MinTrades >= 1 && double.IsFinite(MarginR) && MarginR > 0 && MaxPairShare is > 0 and <= 1
        && PositiveQuarterShare is > 0 and <= 1 && Draws >= 100 && Level is > 0.5 and < 1 && BlockSessions >= 2 && HoldOutMonths is >= 1 and <= 60 && MinHoldOutTrades >= 1 && EmbargoSessions >= 0;

    /// <summary>The stable text of the pre-registered settings (invariant culture, fixed order): the options hash of the records. The notional is not part of it.</summary>
    public string Canonical()
    {
        var c = CultureInfo.InvariantCulture;
        return string.Create(
            c,
            $"pairs-study/1;lags={Core.Lags};q={Core.Q:R};minCorr={Core.MinCorrelation:R};hl={Core.HalfLifeMinSessions:R}..{Core.HalfLifeMaxSessions:R};exit={Core.Exit:R};stop={Core.Stop:R};formation={FormationSessions};maxCandidates={MaxCandidates};session={OpenMinute}/{LastEntryMinute}/{SquareOffMinute}/{BarMinutes};minTradesQ={MinTradesPerQuarter};quarters={MinQuarters};trades={MinTrades};margin={MarginR:R};share={MaxPairShare:R};pos={PositiveQuarterShare:R};draws={Draws};seed={Seed};level={Level:R};block={BlockSessions};holdout={HoldOutMonths}/{MinHoldOutTrades}/{EmbargoSessions};gates={Gates.MinOosTrades}/{Gates.MinExpectancyR:R}/{Gates.MinProfitFactor:R}/{Gates.MinPositiveFoldsPct:R}/{Gates.MaxDrawdownR:R}/{Gates.MaxCalibrationErrorPp:R}");
    }
}
