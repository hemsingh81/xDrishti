using XDrishti.Domain.Market;

namespace XDrishti.Domain.Research;

/// <summary>An instrument as the tick check sees it (spec P3-04b1, E8): its id, symbol and type, and the tick as the instrument master stored it (the master's unit, paise by ADR 0038 item 6); null when the master has none.</summary>
public sealed record TickProbe(Guid Id, string Symbol, InstrumentType Type, decimal? StoredTick);
