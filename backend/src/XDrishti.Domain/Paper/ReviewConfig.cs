namespace XDrishti.Domain.Paper;

/// <summary>What changed in the configuration around the window (spec P10-03), read from the governance record.</summary>
/// <param name="Started">Epochs that started inside the window.</param>
/// <param name="RiskDown">Risk-down changes noted in the epoch.</param>
/// <param name="Unexplained">Changes with no explanation and no note.</param>
public sealed record ReviewConfig(IReadOnlyList<ReviewEpoch> Started, int RiskDown, int Unexplained);
