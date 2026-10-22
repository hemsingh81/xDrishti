namespace XDrishti.Domain.Lab;

/// <summary>One gate of a verdict: the configured value, the value after the multiple-testing adjustment, the actual number, whether it passed and a reason with the numbers.</summary>
public sealed record GateResult(string Name, double Required, double Adjusted, double Actual, bool Passed, string Reason);
