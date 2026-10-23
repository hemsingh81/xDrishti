namespace XDrishti.Domain.Learning;

/// <summary>One entry-time bucket of a cell (P6-14).</summary>
/// <param name="StartMinute">First minute of the bucket, minutes since India midnight.</param>
/// <param name="EndMinute">The minute after the bucket (at most 15:30).</param>
/// <param name="N">Trades whose decision minute lies in the bucket.</param>
/// <param name="Wins">Of them with label 1.</param>
/// <param name="MeanR">Mean R at resolution; null with no trades.</param>
/// <param name="ShrunkRate">Win rate shrunk toward the cell's own with the prior strength.</param>
public sealed record EntryWindowBucket(int StartMinute, int EndMinute, int N, int Wins, double? MeanR, double ShrunkRate);
