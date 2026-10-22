using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Lab;

/// <summary>The test ledger of one strategy family (spec P5-03); <c>lab.not_found</c> when the family is not stored.</summary>
public sealed record GetLedgerQuery(string StrategyId) : IQuery<Result<LedgerView>>;
