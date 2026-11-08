using XDrishti.Application.Abstractions.Research;
using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Research;

/// <summary>The host's <see cref="CostStressOptions"/> as the port a simulation run reads (ADR 0230).</summary>
internal sealed class CostStressSettings(CostStressOptions options) : ICostStressSettings
{
    public CostStressOptions Current { get; } = options;
}
