namespace XDrishti.Domain.Learning;

/// <summary>One scorer against the rung below it on the same out-of-sample rows (CF-10 part 10c-1): the paired day-cluster bootstrap of the log-loss gain and the fold sign test, shown beside each other.</summary>
/// <param name="Scorer">The scorer judged.</param>
/// <param name="Below">The scorer of the rung below.</param>
/// <param name="Bootstrap">The bootstrap, or null when it could not be computed (then <paramref name="Unavailable"/> says why).</param>
/// <param name="Unavailable">The failure code when the bootstrap could not be computed.</param>
/// <param name="Reading">The reading of the probability; <see cref="PairedReading.Below"/> when there is none (an unavailable figure is never a pass).</param>
/// <param name="Sign">The fold sign test over the per-fold log-loss gain.</param>
public sealed record RungComparison(string Scorer, string Below, PairedBootstrapResult? Bootstrap, string? Unavailable, PairedReading Reading, SignTestResult Sign);
