namespace XDrishti.Indicators;

/// <summary>A problem with a parameter combination that a range check cannot see (for example <c>fast ≥ slow</c>): which parameter to blame (0 to 3) and what to tell the caller.</summary>
internal readonly record struct SpecProblem(int Parameter, string Message);
