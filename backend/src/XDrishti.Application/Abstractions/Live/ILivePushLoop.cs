namespace XDrishti.Application.Abstractions.Live;

/// <summary>The background work behind <see cref="ILiveSubscriptions"/> (the host runs it for the lifetime of the process): listens for <c>NOTIFY live_prices</c>, pushes the changed prices (at most once a second), the feed status and ends connections whose session ended.</summary>
public interface ILivePushLoop
{
    /// <summary>Runs until cancelled. Survives failures of the database or of a connection: it logs the kind of failure and carries on.</summary>
    Task RunAsync(CancellationToken cancellationToken);
}
