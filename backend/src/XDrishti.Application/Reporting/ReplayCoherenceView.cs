namespace XDrishti.Application.Reporting;

/// <summary><c>state</c> is ok, entry_mismatch or no_bars; <c>entryOnBars</c> is null with no bar.</summary>
public sealed record ReplayCoherenceView(string State, double EntryStored, double? EntryOnBars, string Detail);
