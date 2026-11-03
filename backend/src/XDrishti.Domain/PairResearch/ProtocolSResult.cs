using XDrishti.Domain.Learning.Extensions;

namespace XDrishti.Domain.PairResearch;

/// <summary>The development verdict of protocol S, with the first day of the hold-out and how many development trades the embargo dropped.</summary>
public sealed record ProtocolSResult(ProtocolOutcome Outcome, DateOnly? HoldOutStart, int EmbargoDropped);
