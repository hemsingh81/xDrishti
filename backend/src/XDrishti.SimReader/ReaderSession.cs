namespace XDrishti.SimReader;

/// <summary>The 1-minute bars of one India trading date (ascending). The readers that need history take a list of these, oldest first.</summary>
public sealed record ReaderSession(DateOnly Date, IReadOnlyList<ReaderBar> Bars);
