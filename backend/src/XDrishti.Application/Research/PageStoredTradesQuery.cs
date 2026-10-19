using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Common;
using XDrishti.Domain.Research;

namespace XDrishti.Application.Research;

/// <summary>A page of the trades of a stored run in the record's order, with the closed filter (spec P3-10, D7).</summary>
public sealed record PageStoredTradesQuery(string RunKey, TradePageFilter Filter, PageRequest Page) : IQuery<Result<Page<RunTrade>>>;
