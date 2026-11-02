using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Application.Abstractions.Learning;

/// <summary>A stored run and the extraction record of an extension for it, if any (P15-10, protocol X).</summary>
/// <param name="RunKey">The run.</param>
/// <param name="StrategyId">Its strategy.</param>
/// <param name="ClosedTrades">Its closed trades.</param>
/// <param name="Record">The extraction record, or null when the extension was never extracted for the run.</param>
public sealed record ExtensionRunSource(string RunKey, string StrategyId, int ClosedTrades, ExtensionExtractionRecord? Record);
