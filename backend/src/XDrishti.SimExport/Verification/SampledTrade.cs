using XDrishti.Domain.Research;

namespace XDrishti.SimExport.Verification;

/// <summary>A trade the sampler or the cover rule chose: the index of its run in the list the caller gave, and the trade.</summary>
public sealed record SampledTrade(int RunIndex, RunTrade Trade);
