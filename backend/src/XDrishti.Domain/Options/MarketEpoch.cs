namespace XDrishti.Domain.Options;

/// <summary>One row of <c>options.market_epochs</c>: a dated market rule change (a SEBI or exchange rule, a tax), unique on (<see cref="Code"/>, <see cref="EffectiveFrom"/>).</summary>
public sealed record MarketEpoch(string Code, DateOnly EffectiveFrom, string Description, string? SourceUrl, bool Confirmed);
