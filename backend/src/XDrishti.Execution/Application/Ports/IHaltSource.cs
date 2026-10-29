using XDrishti.Execution.Domain;

namespace XDrishti.Execution.Application.Ports;

/// <summary>Whether a halt is active. Until P12-05 the only implementation answers <see cref="HaltReading.Unreadable"/>, which blocks every entry.</summary>
public interface IHaltSource
{
    Task<HaltReading> ReadAsync(CancellationToken cancellationToken);
}

internal sealed class ClosedHaltSource : IHaltSource
{
    public Task<HaltReading> ReadAsync(CancellationToken cancellationToken) => Task.FromResult(HaltReading.Unreadable);
}
