using XDrishti.Application.Abstractions.Lab;
using XDrishti.Application.Abstractions.Learning;
using XDrishti.Domain.Lab;

namespace XDrishti.Infrastructure.Learning;

/// <summary>Whether a Lab run is running now (spec P8-18 Part B, criterion 10): the replay time is printed with "other heavy jobs were running". Reads only.</summary>
internal sealed class LabHeavyJobProbe(ILabRunStore labRuns) : IHeavyJobProbe
{
    public async ValueTask<bool> AnyOtherRunningAsync(CancellationToken cancellationToken) =>
        (await labRuns.ListAsync(null, LabRunState.Running, 1, cancellationToken)).Count > 0;
}
