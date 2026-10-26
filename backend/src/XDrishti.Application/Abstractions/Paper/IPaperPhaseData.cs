using XDrishti.Application.Paper;

namespace XDrishti.Application.Abstractions.Paper;

/// <summary>The raw read behind the paper phase check (spec P10-06, ADR 0123): the capture rows of a window. Select only; the port has no write member.</summary>
public interface IPaperPhaseData
{
    /// <summary>Every capture row whose session lies in the window, oldest first.</summary>
    ValueTask<IReadOnlyList<CaptureRunView>> CaptureRunsAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken);
}
