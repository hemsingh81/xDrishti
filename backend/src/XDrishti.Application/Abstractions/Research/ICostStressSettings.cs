using XDrishti.Domain.Confidence;

namespace XDrishti.Application.Abstractions.Research;

/// <summary>The <c>Confidence:CostStress</c> settings a simulation run reads (CF-05, ADR 0230): the tier bps of conservative slippage and the threshold of the filter <c>min_turnover</c>. A port, so the use case takes ports only.</summary>
public interface ICostStressSettings
{
    /// <summary>The validated settings of the host.</summary>
    CostStressOptions Current { get; }
}
