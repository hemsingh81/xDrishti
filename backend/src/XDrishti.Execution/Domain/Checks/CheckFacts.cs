namespace XDrishti.Execution.Domain.Checks;

/// <summary>A value with the time it was read. A missing stamp, a future stamp or an old one makes the checks that read it <see cref="CheckOutcome.Unavailable"/>.</summary>
public sealed record Stamped<T>(T Value, DateTimeOffset At);

public enum TicketState
{
    Armed,
    Approved,
    Invalidated,
    Expired,
    Lapsed,
}

/// <summary>The plan ticket behind the intent: its state, who approved it ("owner", or "auto" with its settings snapshot) and the allocation for this account.</summary>
public sealed record TicketFacts(TicketState State, string? ApprovalKind, bool AutoSnapshotPresent, double? AllocatedQuantity);

/// <summary>The instrument as the intent names it. Numbers are plain doubles: NaN, infinity and non-positive values are unavailable, never a pass.</summary>
public sealed record IntentFacts(
    string? Side,
    string? Segment,
    string? Product,
    string? SecurityId,
    double? Entry,
    double? Stop,
    double? Target,
    double? Quantity,
    double? Tick,
    TicketFacts? Ticket);

/// <summary>A live quote: <c>OpposingDepth</c> is the sum of the five visible levels on the side the order would take.</summary>
public sealed record QuoteFacts(double Ltp, double Bid, double Ask, double UpperCircuit, double LowerCircuit, double? OpposingDepth);

public sealed record InstrumentFacts(bool IsinMatches, bool TradingAllowed, bool Suspended);

/// <summary>Market and instrument inputs. <c>FreezeQuantity</c> null means "no source exists for cash equity" (the own cap applies); the Soft inputs have no source today and read null.</summary>
public sealed record MarketFacts(
    Stamped<QuoteFacts>? Quote,
    InstrumentFacts? Instrument,
    bool? TradingDay,
    double? FreezeQuantity,
    double? AverageDailyVolume,
    int? DaysToResults,
    bool? InFnoBan,
    bool? UnderSurveillance,
    double? MaxCorrelation,
    bool? DataQualityOk);

/// <summary>The system state. Every field may be null (not read): that is unavailable, never a pass.</summary>
public sealed record SystemFacts(
    ModeAnswer? Mode,
    ExecEnvironment Environment,
    HaltReading? ExecutionHalt,
    HaltReading? LiveHalt,
    Stamped<bool>? KillSwitchOff,
    Stamped<DateTimeOffset>? TokenExpiry,
    Stamped<bool>? StaticIpAllowed,
    Stamped<bool>? StreamConnected,
    DateTimeOffset? LastOrderPollAt,
    bool? CanaryPending,
    int? OrdersToday,
    bool? LimiterHasRoom);

/// <summary>The risk profile of the account (percent of capital; counts).</summary>
public sealed record ProfileFacts(double RiskPerTradePct, double DailyLossR, double DailyLossPct, double MaxTotalRiskPct, int MaxOpenPositions, int MaxNewEntriesPerDay, int MaxPerSector);

public sealed record MarginFacts(double TotalMargin, bool InsufficientBalance);

/// <summary>Broker positions and orders: risk at their stops, count, and activity in the instrument (<c>ForeignDetail</c> names an order or position that is not ours).</summary>
public sealed record BookFacts(double PositionRiskInr, int OpenPositions, bool PositionInSymbol, bool WorkingOrderInSymbol, string? ForeignDetail);

/// <summary>Account inputs. <c>TodayPnl</c> is realised plus unrealised (negative is a loss).</summary>
public sealed record AccountFacts(
    double? Capital,
    ProfileFacts? Profile,
    Stamped<double>? FundsAvailable,
    Stamped<MarginFacts>? Margin,
    double? ReservedMargin,
    double? IntentOpenRiskInr,
    Stamped<BookFacts>? Book,
    Stamped<double>? TodayPnl,
    int? EntriesToday,
    int? OpenIntentEntries,
    bool? IntentInSymbol,
    string? Sector,
    int? SectorPositions,
    bool? OpenTradeExists,
    bool? HeldLongAnyAccount);
