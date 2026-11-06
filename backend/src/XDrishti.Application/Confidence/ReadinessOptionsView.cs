using XDrishti.Application.Abstractions.Confidence;
using XDrishti.Domain.Readiness;

namespace XDrishti.Application.Confidence;

/// <summary>The readiness settings as the API shows them: the owner's switch beside the thresholds' text and hash. A thin wrapper so that the handlers do not each build the view.</summary>
internal sealed class ReadinessOptionsView(ReadinessOptions options)
{
    public ReadinessSettingsView View(ReadinessSwitch current) => new(current.Enforce, current.Id, options.SnapshotHash(), options.KeyText());
}
