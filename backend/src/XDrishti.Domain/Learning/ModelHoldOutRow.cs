namespace XDrishti.Domain.Learning;

/// <summary>One candidate of the locked hold-out scored by a model (CF-10e part 3, ADR 0285): the day, the model's probability, whether the trade won and its realised R, and the cell's own stored win rate at the start of the block.</summary>
/// <param name="Key">The trade key (stable order and the seed).</param>
/// <param name="Day">The trade day (the unit the bootstrap resamples).</param>
/// <param name="PWin">The model's probability, finite and in [0, 1].</param>
/// <param name="Won">Whether the realised R was above zero.</param>
/// <param name="R">The realised net R of the trade.</param>
/// <param name="CellRate">The win rate of the trade's cell in the newest stored cell statistics before the block, in (0, 1).</param>
public sealed record ModelHoldOutRow(string Key, DateOnly Day, double PWin, bool Won, double R, double CellRate);
