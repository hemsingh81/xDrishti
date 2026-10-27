using XDrishti.Domain.Market.Calendar;

namespace XDrishti.Domain.Research;

/// <summary>
/// What a run is, apart from its instruments: the strategy, the engine version, the window, the options, the acknowledgement of CSV-adjusted series, the fill rules as canonical JSON (<see cref="FillRules.ToCanonicalJson"/> of the
/// rules actually used; <see cref="FillRules.Default"/> when the request gave none), the universe and the calendar the run read. <see cref="RunAssembler"/> adds the instruments, the totals and the run key.
/// </summary>
public sealed record RunHeader(
    StrategyRecord Strategy,
    string EngineVersion,
    DateOnly From,
    DateOnly To,
    ReplayOptions Options,
    bool AcknowledgeCsvAdjusted,
    string Fills,
    UniverseRecord Universe,
    TradingCalendar Calendar,
    CostRecord? Costs = null,
    string? ContextSha256 = null);
