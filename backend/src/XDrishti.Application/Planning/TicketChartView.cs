namespace XDrishti.Application.Planning;

/// <summary>The daily context of one ticket (spec P8-14, ADR 0113): the last bars up to the planning session, the levels the ticket carries, and its rule texts. Read only; never a price after the session.</summary>
/// <param name="Symbol">The ticket's symbol.</param>
/// <param name="Resolution">exact, ambiguous or not_found (the symbol against the instrument master).</param>
/// <param name="InstrumentId">The instrument when exact.</param>
/// <param name="Session">The last closed session of the plan: the levels' session, else the trading day before the plan date.</param>
/// <param name="Timeframe">1D.</param>
/// <param name="Adjusted">True: adjusted by the corporate actions applied as of the session.</param>
/// <param name="BarCount">Bars returned (fewer than asked when the history is short; never padded).</param>
/// <param name="Bars">Oldest first.</param>
/// <param name="Levels">The ticket's numbers, a straight copy.</param>
/// <param name="Rules">The three rule texts and the validity.</param>
/// <param name="Limits">Honest limits in words.</param>
public sealed record TicketChartView(
    string Symbol,
    string Resolution,
    Guid? InstrumentId,
    DateOnly Session,
    string Timeframe,
    bool Adjusted,
    int BarCount,
    IReadOnlyList<TicketChartBar> Bars,
    TicketChartLevels Levels,
    TicketChartRules Rules,
    IReadOnlyList<string> Limits);

/// <summary>One daily bar.</summary>
/// <param name="T">The India date.</param>
/// <param name="O">Open.</param>
/// <param name="H">High.</param>
/// <param name="L">Low.</param>
/// <param name="C">Close.</param>
/// <param name="V">Volume.</param>
public sealed record TicketChartBar(DateOnly T, double O, double H, double L, double C, long V);

/// <summary>The numbers of the ticket (P7-19) as the chart draws them; <c>Source</c> is <c>none</c> when the ticket carries none.</summary>
public sealed record TicketChartLevels(decimal? EntryRef, decimal? Stop, string? StopAnchor, decimal? Target, decimal? RewardRisk, DateOnly? Session, string? Basis, string Source);

/// <summary>The text rules of the ticket.</summary>
public sealed record TicketChartRules(string Trigger, string Stop, string Target, string Validity);
