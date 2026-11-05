namespace XDrishti.Domain.Learning;

/// <summary>
/// The three arms of the loop test on the same candidates (spec CF-12, D4; ADR 0218): the frozen champion (A), the retrained champion now (B) and no model (C, take every candidate).
/// </summary>
/// <param name="Candidates">Candidates in the common set (those the retrained arm scored).</param>
/// <param name="FrozenMean">Mean value per candidate of arm A; null when arm A could not score the whole set.</param>
/// <param name="RetrainedMean">Mean value per candidate of arm B.</param>
/// <param name="NoMlMean">Mean value per candidate of arm C.</param>
/// <param name="RetrainedVsFrozen">Is the loop (retraining) helping: B minus A.</param>
/// <param name="RetrainedVsNoMl">Is the model helping at all: B minus C.</param>
public sealed record ThreeArmResult(int Candidates, double? FrozenMean, double? RetrainedMean, double? NoMlMean, ArmComparison RetrainedVsFrozen, ArmComparison RetrainedVsNoMl);
