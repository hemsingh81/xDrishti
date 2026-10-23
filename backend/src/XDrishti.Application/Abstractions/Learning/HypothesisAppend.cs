namespace XDrishti.Application.Abstractions.Learning;

/// <summary>The result of an append: the row id (which is the hypothesis id of a proposal) and the status.</summary>
/// <param name="Id">The new row's id; 0 unless created.</param>
/// <param name="Status">What happened.</param>
public sealed record HypothesisAppend(long Id, HypothesisAppendStatus Status);
