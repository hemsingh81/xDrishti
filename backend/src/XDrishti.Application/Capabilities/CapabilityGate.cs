using XDrishti.Application.Abstractions.Capabilities;
using XDrishti.Domain.Capabilities;
using XDrishti.Domain.Common;

namespace XDrishti.Application.Capabilities;

/// <summary>The generic gate over the log (spec P8-18 Part A, ADR 0128): ON only when the capability is on the whitelist and its newest row says on. It reads the log and nothing else.</summary>
internal sealed class CapabilityGate(ICapabilityLog log) : ICapabilityGate
{
    public async ValueTask<bool> IsEnabledAsync(string name, CancellationToken cancellationToken)
    {
        if (!Domain.Capabilities.Capabilities.IsKnown(name))
        {
            return false;
        }

        var newest = await log.NewestAsync(cancellationToken);
        return newest.Any(e => string.Equals(e.Name, name, StringComparison.Ordinal) && e.Enabled);
    }

    public async ValueTask<Result> RequireAsync(string name, CancellationToken cancellationToken) =>
        await IsEnabledAsync(name, cancellationToken) ? Result.Success() : Result.Failure(CapabilityErrors.Off(name));
}
