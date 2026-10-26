using XDrishti.Domain.Market.Context;

namespace XDrishti.Application.Abstractions.Market.Context;

/// <summary>An instrument the context is built from, with its canonical symbol.</summary>
public sealed record ContextInstrument(Guid Id, string Symbol);

/// <summary>
/// The daily rows of the two indices. An index is null when it is not usable: not in the master, not tracked, or without any daily row (the coverage says <c>index_not_tracked</c> or <c>vix_not_tracked</c> and nothing is written).
/// </summary>
public sealed record ContextLegs(ContextInstrument? Index, ContextInstrument? Vix, IReadOnlyList<DailyLeg> IndexLegs, IReadOnlyList<DailyLeg> VixLegs);
