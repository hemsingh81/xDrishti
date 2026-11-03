namespace XDrishti.Application.Pairs;

/// <summary>What a pairs run did, as lines the verb prints: the ledger count and its factor, each quarter's formation, each variant's trades and counters, and the stated limits. No decision reads it.</summary>
public sealed record PairsRunSummary(IReadOnlyList<string> Lines, int Trials, double Factor, int FormationsWritten, int TradesWritten);
