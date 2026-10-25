namespace XDrishti.Application.Reporting;

/// <summary>An instrument left out; Reason is no_bars, too_few_bars, starts_late, ends_early, discontinuity or bad_price.</summary>
public sealed record BenchmarkExclusionView(Guid InstrumentId, string Symbol, string Reason);
