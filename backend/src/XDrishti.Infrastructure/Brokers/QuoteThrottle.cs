using XDrishti.Application.Market.Bars.Dhan;

namespace XDrishti.Infrastructure.Brokers;

/// <summary>
/// The throttle of Dhan's market-quote endpoint: at most one request per second (the documented limit), shared with nothing else — the bar fetch has its own <see cref="RequestThrottle"/>. One instance per process
/// (a singleton) so that two runs that overlap still keep the rate.
/// </summary>
internal sealed class QuoteThrottle(TimeProvider clock)
{
    /// <summary>Dhan's documented rate for the quote API.</summary>
    public const int RequestsPerSecond = 1;

    private readonly RequestThrottle _throttle = new(RequestsPerSecond, clock);

    public ValueTask WaitAsync(CancellationToken cancellationToken) => _throttle.WaitAsync(cancellationToken);
}
