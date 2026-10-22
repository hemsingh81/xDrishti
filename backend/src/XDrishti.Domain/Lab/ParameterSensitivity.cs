namespace XDrishti.Domain.Lab;

/// <summary>The sweep of one parameter and how it was judged: <c>stable</c>, <c>fragile</c>, <c>no_base_edge</c> or <c>inconclusive</c>, with the reason in words.</summary>
public sealed record ParameterSensitivity(string Parameter, IReadOnlyList<SweepResult> Points, string Verdict, string Reason);
