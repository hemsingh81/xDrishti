namespace XDrishti.Domain.Learning.Extensions;

/// <summary>Filings of one kind that are stored with a time (P15-12): the count, the quarters that hold any and the tracked instruments with none.</summary>
public sealed record EventsKindFact(string Kind, int Stored, int Quarters, int InstrumentsWithout);
