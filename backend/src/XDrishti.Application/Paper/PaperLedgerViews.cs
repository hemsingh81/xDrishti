using XDrishti.Application.Reporting;

namespace XDrishti.Application.Paper;

/// <summary>The realised result of a paper account. <see cref="Net"/> is gross less the stored sheet's charges where a closed trade was charged; <see cref="Basis"/> says what it is after and that the sheet is not confirmed here (ISS-25).</summary>
/// <param name="Gross">Rupees before charges.</param>
/// <param name="Charges">Rupees of charges; null when no closed trade was charged.</param>
/// <param name="Net">The result after the stored charges (gross where no sheet charged).</param>
/// <param name="Basis">gross, no charges / after the stored sheet / mixed, in words.</param>
public sealed record PaperRealisedView(decimal Gross, decimal? Charges, decimal Net, string Basis);

/// <summary>The largest fall of the running realised result from its peak.</summary>
/// <param name="Amount">Rupees.</param>
/// <param name="Pct">Percent of the capital plus the peak.</param>
public sealed record PaperDrawdownView(decimal Amount, MetricView Pct);

/// <summary>The sheet that charged the account's closed trades. The confirmation is unreadable here, so <see cref="ConfirmedOn"/> is always null.</summary>
/// <param name="Id">The sheet id; null when no closed trade was charged.</param>
/// <param name="ConfirmedOn">Always null in this view (spec P10-02, criterion 5).</param>
public sealed record PaperChargeSheetView(string? Id, DateOnly? ConfirmedOn);

/// <summary>What the figures rest on.</summary>
/// <param name="ClosedTrades">Closed trades.</param>
/// <param name="OpenTrades">Open positions.</param>
/// <param name="EpochStartedOn">The epoch's first session; null before the first capture.</param>
/// <param name="LastClosedSession">The session the marks and the last-session result are as of.</param>
public sealed record PaperEvidenceView(int ClosedTrades, int OpenTrades, DateOnly? EpochStartedOn, DateOnly LastClosedSession);

/// <summary>One paper account's ledger, every figure derived from its stored trades and labelled as paper (spec P10-02).</summary>
/// <param name="AccountId">The account.</param>
/// <param name="Name">Its name.</param>
/// <param name="EpochId">The epoch; null before the first capture.</param>
/// <param name="Capital">The capital; null when the account has none (BrokerFunds).</param>
/// <param name="CapitalSource">Where it comes from.</param>
/// <param name="Realised">The realised result.</param>
/// <param name="Equity">Capital plus the realised result.</param>
/// <param name="FundsAvailable">Equity less the open notional, at least 0.</param>
/// <param name="OpenRiskPct">Open risk, percent of equity.</param>
/// <param name="OpenCount">Open positions.</param>
/// <param name="Unrealised">Rupees at the marks; unavailable when any mark is, empty without a position.</param>
/// <param name="LastSessionResult">Realised rupees of the last closed session (0 when no trade closed).</param>
/// <param name="LastSessionPct">That result, signed percent of the equity at the start of the session.</param>
/// <param name="MaxDrawdownClosed">Closed-trade drawdown of the realised result.</param>
/// <param name="ChargeSheet">The sheet used.</param>
/// <param name="Evidence">What it rests on.</param>
/// <param name="Limits">The honest limits.</param>
public sealed record PaperAccountView(
    Guid AccountId,
    string Name,
    long? EpochId,
    decimal? Capital,
    string CapitalSource,
    PaperRealisedView Realised,
    MetricView Equity,
    MetricView FundsAvailable,
    MetricView OpenRiskPct,
    int OpenCount,
    MetricView Unrealised,
    decimal LastSessionResult,
    MetricView LastSessionPct,
    PaperDrawdownView MaxDrawdownClosed,
    PaperChargeSheetView ChargeSheet,
    PaperEvidenceView Evidence,
    IReadOnlyList<string> Limits);

/// <summary>The paper accounts with the label every paper response carries.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Items">The accounts.</param>
public sealed record PaperAccountList(string Basis, IReadOnlyList<PaperAccountView> Items);

/// <summary>The mark of an open position: a value with its close and date, or unavailable with the reason and no price.</summary>
/// <param name="State">value or unavailable.</param>
/// <param name="Price">The close; null unless value.</param>
/// <param name="AsOf">The date of the bar read; null when none.</param>
/// <param name="Reason">Why unavailable; null for a value.</param>
public sealed record PaperMarkView(string State, decimal? Price, DateOnly? AsOf, string? Reason);

/// <summary>An open paper position.</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Qty">Quantity.</param>
/// <param name="EntryAt">The fill minute.</param>
/// <param name="EntryPrice">The filled entry.</param>
/// <param name="StopPrice">The initial stop.</param>
/// <param name="Mark">The mark.</param>
/// <param name="Unrealised">Rupees at the mark; unavailable with the mark.</param>
/// <param name="RiskAmount">Rupees at risk to the initial stop.</param>
/// <param name="RiskPctOfEquity">Risk as a percent of equity.</param>
/// <param name="HeldSessions">Sessions held, entry and last closed session included.</param>
public sealed record PaperPositionView(string Symbol, string Side, int Qty, DateTimeOffset EntryAt, decimal EntryPrice, decimal StopPrice, PaperMarkView Mark, MetricView Unrealised, decimal RiskAmount, MetricView RiskPctOfEquity, int HeldSessions);

/// <summary>One closed paper trade in the ledger.</summary>
/// <param name="Symbol">The symbol.</param>
/// <param name="Side">BUY or SELL.</param>
/// <param name="Qty">Quantity.</param>
/// <param name="EntryAt">The fill minute.</param>
/// <param name="ExitAt">The exit minute.</param>
/// <param name="Gross">Rupees before charges.</param>
/// <param name="Charges">Rupees of charges; null when none.</param>
/// <param name="Result">Rupees after the stored charges (gross where none).</param>
/// <param name="R">The trade's R.</param>
public sealed record PaperClosedTradeView(string Symbol, string Side, int Qty, DateTimeOffset EntryAt, DateTimeOffset ExitAt, decimal Gross, decimal? Charges, decimal Result, double R);

/// <summary>The open positions and the closed trades of a paper account.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Limits">The honest limits.</param>
/// <param name="AccountId">The account.</param>
/// <param name="AsOfSession">The session the marks are as of.</param>
/// <param name="Open">Open positions.</param>
/// <param name="Closed">Closed trades, newest exit first.</param>
public sealed record PaperPositionList(string Basis, IReadOnlyList<string> Limits, Guid AccountId, DateOnly AsOfSession, IReadOnlyList<PaperPositionView> Open, IReadOnlyList<PaperClosedTradeView> Closed);

/// <summary>One point of the realised-equity line.</summary>
/// <param name="Date">The session.</param>
/// <param name="Realised">The realised result up to and including the session.</param>
/// <param name="Equity">Capital plus that result.</param>
public sealed record PaperEquityPoint(DateOnly Date, decimal Realised, MetricView Equity);

/// <summary>The realised-equity line of a paper account, one point per trading session from the epoch's start.</summary>
/// <param name="Basis">The label.</param>
/// <param name="Limits">The honest limits.</param>
/// <param name="AccountId">The account.</param>
/// <param name="EpochId">The epoch; null before the first capture.</param>
/// <param name="Points">The points, oldest first.</param>
public sealed record PaperEquityView(string Basis, IReadOnlyList<string> Limits, Guid AccountId, long? EpochId, IReadOnlyList<PaperEquityPoint> Points);

/// <summary>Whether the derived ledger agrees with the stored rows.</summary>
/// <param name="Basis">The label.</param>
/// <param name="AccountId">The account.</param>
/// <param name="EpochId">The epoch; null before the first capture.</param>
/// <param name="Status">ok, or differences.</param>
/// <param name="Differences">Each difference, in words (empty when ok).</param>
public sealed record PaperReconcileView(string Basis, Guid AccountId, long? EpochId, string Status, IReadOnlyList<string> Differences);
