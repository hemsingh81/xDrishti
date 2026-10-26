using XDrishti.Application.Abstractions.Capabilities;

namespace XDrishti.Application.Capabilities;

/// <summary>Builds the view of the capabilities from the log: every name of the whitelist, OFF when it has no row.</summary>
internal sealed class CapabilityViews(ICapabilityLog log)
{
    /// <summary>The most switches shown.</summary>
    public const int HistoryLimit = 20;

    public async ValueTask<CapabilitiesView> BuildAsync(CancellationToken cancellationToken)
    {
        var newest = (await log.NewestAsync(cancellationToken)).ToDictionary(e => e.Name, StringComparer.Ordinal);
        var items = Domain.Capabilities.Capabilities.All
            .Select(c => newest.TryGetValue(c.Name, out var e)
                ? new CapabilityView(c.Name, c.Label, c.Meaning, e.Enabled, e.ChangedAt, e.Reason)
                : new CapabilityView(c.Name, c.Label, c.Meaning, false, null, null))
            .ToList();
        var history = (await log.HistoryAsync(HistoryLimit, cancellationToken))
            .Where(e => Domain.Capabilities.Capabilities.IsKnown(e.Name))
            .Select(e => new CapabilityHistoryView(e.Id, e.Name, e.Enabled, e.Actor, e.Reason, e.ChangedAt))
            .ToList();
        return new CapabilitiesView(items, history);
    }
}
