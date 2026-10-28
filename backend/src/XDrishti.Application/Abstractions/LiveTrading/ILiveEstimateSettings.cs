using XDrishti.Domain.LiveTrading;

namespace XDrishti.Application.Abstractions.LiveTrading;

/// <summary>
/// The settings of the execution feedback (spec P11-02, each from its own sub-section of <c>Live</c>): <c>Live:Buckets:EdgesCr</c>, <c>Live:Estimates:MinN</c> / <c>ProposalMinN</c> / <c>Enabled</c> (the first-start seed of the weekly service, OFF) and
/// <c>Live:Capacity:PriorStrength</c> / <c>MinN</c>. None of them changes a trading behaviour.
/// </summary>
public interface ILiveEstimateSettings
{
    bool Enabled { get; }

    BucketEdges Edges { get; }

    int MinN { get; }

    int ProposalMinN { get; }

    int CapacityPriorStrength { get; }

    int CapacityMinN { get; }
}
