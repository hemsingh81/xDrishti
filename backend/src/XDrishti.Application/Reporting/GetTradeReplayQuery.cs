using XDrishti.Application.Abstractions.Messaging;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Reporting;

/// <summary>One stored trade on its bars (P8-11). Timeframe, Before and After are as asked (null: the defaults of the timeframe).</summary>
public sealed record GetTradeReplayQuery(string RunKey, int Ordinal, bool OpenAtEnd, string? Timeframe, int? Before, int? After) : IQuery<Result<TradeReplayView>>;
