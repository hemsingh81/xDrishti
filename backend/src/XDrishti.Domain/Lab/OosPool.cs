namespace XDrishti.Domain.Lab;

/// <summary>One pooled out-of-sample window: the test window of a fold, the trades of the walk-forward winners of every cell that entered in it, and their summed R.</summary>
public sealed record OosWindow(DateOnly TestStart, DateOnly TestEnd, int Trades, double TotalR);

/// <summary>The out-of-sample numbers of a full test and the windows they came from (spec P5-04, assumption 3).</summary>
public sealed record OosPool(OosEvidence Evidence, IReadOnlyList<OosWindow> Windows);
