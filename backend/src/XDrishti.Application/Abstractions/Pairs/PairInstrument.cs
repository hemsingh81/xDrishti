namespace XDrishti.Application.Abstractions.Pairs;

/// <summary>A share of the pairs universe (P14-13): an F&amp;O underlying that is not retired, with today's sector, the master's tick in paise (null: none) and whether today's ASM/GSM flag is set (a dated history does not exist).</summary>
public sealed record PairInstrument(Guid Id, string Symbol, string? Sector, decimal? TickSizePaise, bool AsmGsm);
