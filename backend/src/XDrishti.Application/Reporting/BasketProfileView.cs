namespace XDrishti.Application.Reporting;

/// <summary>
/// The performance profile of one basket (BK-07, metrics m1b to m16, m23): its own prices against an index, with every figure a <see cref="MetricView"/> (a number only for a value) and the basis printed. Nothing here is a trading result and none is a forecast.
/// </summary>
/// <param name="Basket">Who it is.</param>
/// <param name="Window">The window and where each end came from.</param>
/// <param name="MembersAsOf">The India date of the member list.</param>
/// <param name="MembershipBasis"><c>dated</c> (membership history exists) or <c>single_snapshot</c> (members as of one date, not as of each trading day; survivorship applies, ISS-22).</param>
/// <param name="MembershipFrom">The first date with membership history.</param>
/// <param name="Members">The members counted and the ones left out, named.</param>
/// <param name="Index">The index the basket is set against.</param>
/// <param name="Hold">The basket's buy-and-hold price return in percent (P8-07).</param>
/// <param name="Series">The daily series of the charts; the same numbers are in the tables.</param>
/// <param name="Risk">Volatility, drawdown, beta, tracking and capture.</param>
/// <param name="IndexDifference">The passive difference to the index; never a trading result and never called alpha (BK-07 review).</param>
/// <param name="Breadth">Breadth inside the basket.</param>
/// <param name="Dispersion">Dispersion of member returns.</param>
/// <param name="Correlation">Average correlation, effective bets and the matrix.</param>
/// <param name="Contribution">Who made the return.</param>
/// <param name="Regime">The index by regime.</param>
/// <param name="Turnover">Member turnover.</param>
/// <param name="Capacity">Liquidity and capacity.</param>
/// <param name="HoldOut">The median split of the strategy trades of this basket (m23).</param>
/// <param name="RelativeStrength">The relative strength returns (m8); the rank is on the ranking board.</param>
/// <param name="Evidence">The evidence block.</param>
public sealed record BasketProfileView(
    ProfileBasketView Basket,
    BenchmarkWindowView Window,
    DateOnly MembersAsOf,
    string MembershipBasis,
    DateOnly? MembershipFrom,
    ProfileMembersView Members,
    ProfileIndexView Index,
    MetricView Hold,
    ProfileSeriesView Series,
    ProfileRiskView Risk,
    IndexDifferenceView IndexDifference,
    ProfileBreadthView Breadth,
    ProfileDispersionView Dispersion,
    ProfileCorrelationView Correlation,
    ProfileContributionView Contribution,
    ProfileRegimeView Regime,
    ProfileTurnoverView Turnover,
    ProfileCapacityView Capacity,
    ProfileHoldOutView HoldOut,
    IReadOnlyList<RelativeStrengthReturnView> RelativeStrength,
    BenchmarkEvidenceView Evidence);

/// <summary>The basket: <c>Kind</c> is manual, portfolio, index, list or derived; <c>ParamsHash</c> only for a derived basket.</summary>
public sealed record ProfileBasketView(Guid Id, string Name, string Kind, string? ParamsHash);

/// <summary>Instruments in the basket in the window (<c>Total</c>), the ones the figures rest on (<c>Counted</c>) and every one left out with its reason (no_bars, starts_late, ends_early, discontinuity, bad_price, too_few_bars).</summary>
public sealed record ProfileMembersView(int Total, int Counted, IReadOnlyList<ProfileExclusionView> Excluded);

/// <summary>An instrument left out and why.</summary>
public sealed record ProfileExclusionView(string Symbol, string Reason);

/// <summary>The index in force: null symbol when there is none.</summary>
public sealed record ProfileIndexView(Guid? Id, string? Symbol, bool IsDefault);

/// <summary>The series of the charts, one entry per index date. The basket and the index are rebased to 100 on the first date; an entry without a value is null (the index has no bar that day), never zero.</summary>
public sealed record ProfileSeriesView(
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<double> Basket,
    IReadOnlyList<double?> Index,
    IReadOnlyList<double?> RelativeStrength,
    IReadOnlyList<double> Drawdown,
    IReadOnlyList<double?> RollingBeta60);

/// <summary>Risk figures (fractions, not percent). Alpha, tracking error and information ratio are those of the 252-day window; <c>Alpha</c> is the regression intercept against the index, not a trading result.</summary>
public sealed record ProfileRiskView(
    MetricView Vol20,
    MetricView Vol60,
    MetricView Vol252,
    MetricView Mdd,
    MetricView MddDuration,
    bool DrawdownOpen,
    int DaysSincePeak,
    MetricView Beta60,
    MetricView Beta252,
    MetricView Alpha,
    MetricView TrackingError,
    MetricView InformationRatio,
    MetricView UpCapture,
    MetricView DownCapture);

/// <summary>The basket's hold return, the index's over the same dates and their difference, all in percent and all passive.</summary>
public sealed record IndexDifferenceView(MetricView Basket, MetricView Index, MetricView Difference);

/// <summary>One breadth share with the members behind it.</summary>
public sealed record BreadthFigureView(MetricView Share, int Voters, IReadOnlyList<string> Excluded, string ExcludedReason);

/// <summary>Breadth inside the basket on the last session with a bar; <c>SeriesB20</c> is the share above the 20-day average per day (null where too few voted).</summary>
public sealed record ProfileBreadthView(
    BreadthFigureView B20,
    BreadthFigureView B50,
    BreadthFigureView B200,
    BreadthFigureView AdShare,
    BreadthFigureView NewHighShare,
    IReadOnlyList<string> NoBar,
    IReadOnlyList<DateOnly> SeriesDates,
    IReadOnlyList<double?> SeriesB20);

/// <summary>Dispersion of member returns on the last session, its mean over 20 sessions and the interquartile range.</summary>
public sealed record ProfileDispersionView(MetricView Today, MetricView Mean20, MetricView Iqr, int Voters);

/// <summary>Average pairwise correlation, the effective number of bets (a heuristic) and the matrix of the most liquid members (null when there is no correlation figure).</summary>
public sealed record ProfileCorrelationView(MetricView RhoBar, MetricView NEff, int Members, int Pairs, bool Sampled, CorrelationMatrixView? Matrix);

/// <summary>The matrix: <c>Total</c> members behind the figure, <c>Symbols</c> those drawn (at most 40, the most liquid, in sector order); a pair without enough common sessions is null.</summary>
public sealed record CorrelationMatrixView(int Total, IReadOnlyList<string> Symbols, IReadOnlyList<IReadOnlyList<double?>> Rho);

/// <summary>One member's part of the hold return.</summary>
public sealed record ContributionRowView(string Symbol, double ReturnPct, double Share);

/// <summary>Who made the return: the five largest and five smallest parts, the share of the three largest and every member (empty unless the shares exist).</summary>
public sealed record ProfileContributionView(MetricView Top3Share, int Included, IReadOnlyList<ContributionRowView> Top, IReadOnlyList<ContributionRowView> Bottom, IReadOnlyList<ContributionRowView> Rows);

/// <summary>The index on days that followed one regime of the previous session.</summary>
public sealed record RegimeRowView(int Code, string Label, int Days, MetricView MeanReturn, MetricView Vol, MetricView HitRate, string State);

/// <summary>The regime panel: <c>Status</c> is a value when a regime was recorded, else why not; <c>Unknown</c> days sit in no row.</summary>
public sealed record ProfileRegimeView(MetricView Status, int Unknown, int MinDays, IReadOnlyList<RegimeRowView> Rows);

/// <summary>Member turnover over the window.</summary>
public sealed record ProfileTurnoverView(MetricView Turnover, int Adds, int Removes);

/// <summary>What the basket can absorb: the ADV20 (the CF-05 median of traded value, in rupees) of the members, a share of it as capacity and the flag shares. A member with fewer than 20 sessions of volume is listed, never guessed.</summary>
public sealed record ProfileCapacityView(MetricView MedianAdv, MetricView P10Adv, MetricView BasketCapacity, MetricView AsmGsmShare, MetricView FnoShare, int Counted, IReadOnlyList<string> ShortMembers, double CapacityPct);

/// <summary>The median split of the strategy trades of the basket in the default run set (m23): not the locked hold-out of ADR 0207. <c>Status</c> says whether trades could be read.</summary>
public sealed record ProfileHoldOutView(MetricView Status, int Trades, int MinPerHalf, IReadOnlyList<ProfileHoldOutCellView> Cells);

/// <summary>One strategy's cell: the n and the expectancy of each half and the verdict (agrees, disagrees, insufficient, not_evaluable).</summary>
public sealed record ProfileHoldOutCellView(string Strategy, DateOnly? SplitDate, int FirstN, int SecondN, MetricView FirstExpectancy, MetricView SecondExpectancy, string Verdict);

/// <summary>The relative strength return over a window of sessions (fraction).</summary>
public sealed record RelativeStrengthReturnView(int Window, MetricView Return);
