namespace XDrishti.Domain.Paper;

/// <summary>How many tickets of the epoch count and how many were set apart, by reason (spec P10-04, criterion 1).</summary>
/// <param name="Counted">Closed, primary, measured tickets.</param>
/// <param name="Reserve">Reserve tickets.</param>
/// <param name="Straddle">Primary tickets set aside because they straddle a change.</param>
/// <param name="Open">Primary measured tickets still open.</param>
/// <param name="OtherEpochs">Entries of other epochs.</param>
public sealed record CompareCounts(int Counted, int Reserve, int Straddle, int Open, int OtherEpochs);
