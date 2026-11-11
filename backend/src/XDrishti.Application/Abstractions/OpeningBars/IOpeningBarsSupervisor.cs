namespace XDrishti.Application.Abstractions.OpeningBars;

/// <summary>The whole life of the opening-minute recorder in the feed host, until the host stops.</summary>
public interface IOpeningBarsSupervisor
{
    Task RunAsync(CancellationToken cancellationToken);
}
