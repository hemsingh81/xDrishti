namespace XDrishti.Strategies.File;

/// <summary>
/// A problem of a strategy file and where it is (P3-02c D5). <paramref name="Path"/> is the path in the model (<c>buy.trigger[1]</c>, <c>exits[0].rr[1]</c>, <c>stop.lookback</c>; empty for the document), so that the problems of
/// <c>StrategyCompiler</c> map straight through. Lines and columns are 1-based; the end is the end of the node. <paramref name="Code"/> is a stable <c>file.*</c> string.
/// </summary>
public sealed record FileProblem(string Path, int Line, int Column, int EndLine, int EndColumn, string Code, ProblemSeverity Severity, string Message);
