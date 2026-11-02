namespace XDrishti.Application.Abstractions.Learning;

/// <summary>Where protocol P gets its events (P15-10). Nothing implements it before the PEAD strategy exists: the registered default answers no events.</summary>
public interface IPeadEventSource
{
    /// <summary>The events.</summary>
    ValueTask<PeadEventSet> ReadAsync(CancellationToken cancellationToken);
}
