namespace XDrishti.Application.Live;

/// <summary>
/// The wait before a reconnect (P2-10 criterion 5): 1, 2, 4 ... seconds up to the maximum, each with a jitter of up to a fifth added (so that a feed that is restarted together with the network does not hit Dhan in step), never above the maximum.
/// <see cref="Reset"/> after a connection that delivered data. The random number is injectable (a value in [0, 1)), so tests are exact.
/// </summary>
internal sealed class ReconnectBackoff(Func<double>? random = null)
{
    /// <summary>The largest fraction of the wait that is added as jitter.</summary>
    public const double JitterFraction = 0.2;

    private readonly Func<double> _random = random ?? Random.Shared.NextDouble;
    private int _attempt;

    /// <summary>The wait for the next attempt (and moves on to the following one).</summary>
    public TimeSpan Next(TimeSpan initial, TimeSpan max)
    {
        var exponent = Math.Min(_attempt, 20);
        _attempt++;
        var baseSeconds = Math.Min(initial.TotalSeconds * Math.Pow(2, exponent), max.TotalSeconds);
        var jittered = baseSeconds * (1 + (JitterFraction * Math.Clamp(_random(), 0d, 1d)));
        return TimeSpan.FromSeconds(Math.Min(jittered, max.TotalSeconds));
    }

    /// <summary>The next wait starts from the beginning again.</summary>
    public void Reset() => _attempt = 0;
}
