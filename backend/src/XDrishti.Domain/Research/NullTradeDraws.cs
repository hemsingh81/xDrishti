namespace XDrishti.Domain.Research;

/// <summary>What the random-entry walk gave for one real trade: net R of its random trade in each seed (NaN where the draw was dropped after the redraws) and the redraws it took; or the reason the trade could not be restated.</summary>
/// <param name="TradeId"><see cref="NullTradeSpec.Id"/>.</param>
/// <param name="NetR">One value per seed, NaN for a dropped draw; empty when <paramref name="Unavailable"/> is set.</param>
/// <param name="Redraws">Redraws spent over all seeds.</param>
/// <param name="Unavailable">Why the trade could not be restated (no bars for its session, no tick, no cost sheet, a walk refused); null when it could.</param>
public sealed record NullTradeDraws(int TradeId, IReadOnlyList<double> NetR, int Redraws, string? Unavailable);
