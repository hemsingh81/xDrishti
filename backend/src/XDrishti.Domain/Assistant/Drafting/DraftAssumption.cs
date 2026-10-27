namespace XDrishti.Domain.Assistant.Drafting;

/// <summary>A value the model chose on its own: the YAML path (such as <c>exits[0].rr</c>), the value it wrote and why.</summary>
public sealed record DraftAssumption(string Field, string Value, string Why);
