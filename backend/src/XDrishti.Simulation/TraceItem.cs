namespace XDrishti.Simulation;

/// <summary>One condition of the strategy at the decision step: its path in the model (<c>buy.trigger[0]</c>), the text as written, its truth and the values of the features it reads.</summary>
public sealed record TraceItem(string Path, string Source, TraceTruth Truth, IReadOnlyList<TraceValue> Values);
