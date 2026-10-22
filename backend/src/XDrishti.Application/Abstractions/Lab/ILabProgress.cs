using XDrishti.Application.Lab;

namespace XDrishti.Application.Abstractions.Lab;

/// <summary>
/// Where the progress of a Lab run goes (spec P5-02, D5; P5-00 D1): one port, so the page, the CLI and the tests see the same events. The host's adapter sends them to the SignalR hub; a missed event costs nothing because the run is
/// served by the API as well.
/// </summary>
public interface ILabProgress
{
    /// <summary>The run moved or advanced. The caller throttles (see <c>ProgressThrottle</c>).</summary>
    ValueTask ProgressAsync(LabRunProgress progress, CancellationToken cancellationToken);

    /// <summary>The run reached a final state; always the last event of a run.</summary>
    ValueTask FinishedAsync(LabRunFinished finished, CancellationToken cancellationToken);
}
