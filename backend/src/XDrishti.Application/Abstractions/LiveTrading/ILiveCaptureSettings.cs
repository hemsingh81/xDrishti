using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// The settings of the live capture (spec P11-01, each from its own sub-section of <c>Live</c>, every switch OFF by default): <c>Live:Capture:Enabled</c> (the first-start seed of the two services), <c>Live:Capture:OrderBook:Enabled</c> (the order book is
/// read only when it is on), <c>Live:Capture:PollSeconds</c>, the tolerances of <c>Live:Match</c> and the stop window of <c>Live:Protect</c>.
/// </summary>
public interface ILiveCaptureSettings
{
    bool Enabled { get; }

    bool OrderBookEnabled { get; }

    int PollSeconds { get; }

    MatchTolerances Tolerances { get; }

    TimeSpan ProtectWithin { get; }
}
