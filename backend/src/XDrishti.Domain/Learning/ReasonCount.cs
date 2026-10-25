namespace XDrishti.Domain.Learning;

/// <summary>A code and how many items carried it.</summary>
/// <param name="Code">The stable code.</param>
/// <param name="Count">Items.</param>
public sealed record ReasonCount(string Code, int Count);
