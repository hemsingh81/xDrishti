namespace XDrishti.Application.Abstractions.Ai;

/// <summary>Whether the nightly service has a run in state running that started within the given window (spec P9-00 D13): while it does, a turn is refused. One indexed read.</summary>
public interface INightlyRunProbe
{
    ValueTask<bool> IsRunningAsync(TimeSpan window, CancellationToken cancellationToken);
}
