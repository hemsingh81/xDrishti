using XDrishti.Domain.Accounts;
using XDrishti.Domain.Common;
using XDrishti.Domain.Selection;
using XDrishti.Domain.Selection.SmartRules;

namespace XDrishti.Application.Abstractions.Planning;

/// <summary>An instrument the plan needs facts about.</summary>
/// <param name="Id">The instrument master id; null when the source of the ticket did not know it.</param>
/// <param name="Symbol">The symbol.</param>
public sealed record ContextInstrument(Guid? Id, string Symbol);

/// <summary>What is known about one instrument. A null part is "not known", never "none".</summary>
/// <param name="Sector">The sector of the instrument master.</param>
/// <param name="Baskets">The names of the baskets that hold it now.</param>
/// <param name="HeldLong">Whether any synced holding of any Portfolio account holds it.</param>
public sealed record InstrumentFacts(string? Sector, IReadOnlyList<string>? Baskets, bool? HeldLong);

/// <summary>A Trading-role account as the plan needs it (the limits already include the profile and the account's overrides). A null number is "not known".</summary>
/// <param name="AccountId">The broker account.</param>
/// <param name="Mode">Paper or manual live. Only paper receives anything.</param>
/// <param name="Equity">Capital the percentages apply to, in rupees.</param>
/// <param name="RiskPerTradePct">Risk of one trade, percent of equity.</param>
/// <param name="MaxCapitalPerTradePct">Most capital in one trade, percent of equity.</param>
/// <param name="MaxTotalRiskPct">Total open risk, percent of equity.</param>
/// <param name="OpenRiskPct">Open risk now, percent of equity.</param>
/// <param name="FundsAvailable">Money the account can use, in rupees.</param>
/// <param name="MaxPerSector">Positions per sector.</param>
/// <param name="DailyLossLimitPct">Daily loss limit, percent.</param>
/// <param name="DailyLossPct">Loss so far today as a positive percent; null when not known.</param>
/// <param name="HeldSymbols">Symbols the account holds; null when not known.</param>
public sealed record ContextAccount(
    Guid AccountId,
    TradingMode Mode,
    double? Equity,
    double? RiskPerTradePct,
    double? MaxCapitalPerTradePct,
    double? MaxTotalRiskPct,
    double? OpenRiskPct,
    double? FundsAvailable,
    int? MaxPerSector,
    double? DailyLossLimitPct,
    double? DailyLossPct,
    IReadOnlySet<string>? HeldSymbols);

/// <summary>What the builder asks for: facts as of the close of <see cref="Session"/> for these instruments.</summary>
/// <param name="Session">The last closed session; nothing after its close may be read.</param>
/// <param name="ForDate">The planned session.</param>
/// <param name="Instruments">The instruments of the ranked candidates.</param>
public sealed record PlanContextRequest(DateOnly Session, DateOnly ForDate, IReadOnlyList<ContextInstrument> Instruments);

/// <summary>
/// Everything the wiring reads besides the tickets. A null part is "no data source": the rule that needs it says so (unavailable) and never counts it as a pass.
/// </summary>
/// <param name="Book">Open positions of the Trading accounts; null when not known (then the fill takes nothing).</param>
/// <param name="Instruments">Facts by symbol (ordinal, upper case).</param>
/// <param name="Accounts">Trading-role accounts.</param>
/// <param name="Correlations">Return correlations up to the close of the session; null when not computable.</param>
/// <param name="Gate">Market figures before the plan date; null when there is no source.</param>
/// <param name="TakenTrades">Real taken trades; null when there is no source.</param>
/// <param name="Notes">Honest-limit sentences to show beside the plan (for example "no position records exist").</param>
/// <param name="MarketNote">What the breadth in <paramref name="Gate"/> is (P3-01e); null when the gate has no breadth. Written after the flag lines of the basis.</param>
/// <param name="BookFromLedger">True when <paramref name="Book"/> is the paper ledger's (P10-02): an empty list is then a known empty book, not the owner's assumption.</param>
public sealed record PlanContext(
    IReadOnlyList<BookPosition>? Book,
    IReadOnlyDictionary<string, InstrumentFacts> Instruments,
    IReadOnlyList<ContextAccount> Accounts,
    CorrelationTable? Correlations,
    MarketGateInput? Gate,
    IReadOnlyList<TakenTrade>? TakenTrades,
    IReadOnlyList<string> Notes,
    bool BookFromLedger = false,
    string? MarketNote = null);

/// <summary>Where the portfolio, account and market facts of a plan come from. Read only: nothing behind it may place, modify or cancel an order or write to an account.</summary>
public interface IPlanContextSource
{
    /// <summary>Reads the facts as of the close of the session, or says why it cannot (then no plan is built).</summary>
    ValueTask<Result<PlanContext>> ReadAsync(PlanContextRequest request, CancellationToken cancellationToken);
}
