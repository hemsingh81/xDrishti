using XDrishti.Application.Abstractions.Market.Bars;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>What one chart window yielded: its bars (in the order Dhan sent them) or why it failed; <see cref="Requests"/> counts the HTTP requests made for it (a retry is a second one).</summary>
internal sealed record ChartWindowResult(IReadOnlyList<Bar> Bars, int Requests, IntradayFailure? Failure);
