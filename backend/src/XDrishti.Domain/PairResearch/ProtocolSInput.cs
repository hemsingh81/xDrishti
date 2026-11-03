namespace XDrishti.Domain.PairResearch;

/// <summary>What protocol S judges: the stored trades of the primary variant, N (the ledger's distinct trials of the family), the grid of every variant, the formation counts and the options. <paramref name="HoldOutStart"/> is the first day of the hold-out when the caller has already cut it (the trades are then development trades only); null: cut from the trades given.</summary>
public sealed record ProtocolSInput(IReadOnlyList<PairTradeSample> Trades, int Trials, IReadOnlyList<PairGridLine> Grid, IReadOnlyList<PairFormationStat> Formations, PairStudyOptions Options, DateOnly? HoldOutStart = null);
