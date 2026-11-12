namespace XDrishti.Domain.Learning;

/// <summary>A graded shadow candidate that one model scored (CF-10e part 10e-1): what the forward figures and the calibration read.</summary>
/// <param name="SessionDate">The session of the candidate.</param>
/// <param name="TradeKey">The trade key (breaks ties, ordinal).</param>
/// <param name="PWin">The model's probability, in [0, 1].</param>
/// <param name="R">The grade's R.</param>
public sealed record ScoredOutcome(DateOnly SessionDate, string TradeKey, double PWin, double R);
