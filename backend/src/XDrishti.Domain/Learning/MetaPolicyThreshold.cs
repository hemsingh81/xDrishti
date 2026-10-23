namespace XDrishti.Domain.Learning;

/// <summary>The threshold of one group (P6-13). <paramref name="Threshold"/> is null when the group had too few scores and takes everything.</summary>
/// <param name="Group">The group (<c>*</c> when not split).</param>
/// <param name="Threshold">Take when the calibrated probability is at least this; 0 takes everything.</param>
/// <param name="Rows">Scores the threshold was chosen on.</param>
/// <param name="KeptShare">Share of those scores it keeps.</param>
public sealed record MetaPolicyThreshold(string Group, double? Threshold, int Rows, double KeptShare);
