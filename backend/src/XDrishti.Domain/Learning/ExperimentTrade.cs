namespace XDrishti.Domain.Learning;

/// <summary>One entry of the baseline run and what the candidate run did with it (P6-11): the same entry, the same day. The candidate may have skipped it, never added one.</summary>
/// <param name="Key">The trade key (<c>strategy|symbol|side|decision date</c>), the same in both runs.</param>
/// <param name="TradeDate">India date of the entry: the date a fold window is cut by.</param>
/// <param name="BaselineR">R of the baseline trade (net of charges when the runs are costed, else filled).</param>
/// <param name="CandidateR">R of the same entry in the candidate run; null when the candidate did not take it.</param>
public sealed record ExperimentTrade(string Key, DateOnly TradeDate, double BaselineR, double? CandidateR);
