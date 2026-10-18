namespace XDrishti.Api.Simulate;

/// <summary>The two streams of the verb: <see cref="Out"/> holds the banner and the summary, <see cref="Error"/> the refusals, the usage text and the log lines (spec P3-04b2, D2). Tests give string writers.</summary>
internal sealed record SimulateConsole(TextWriter Out, TextWriter Error)
{
    /// <summary>The process's console.</summary>
    public static SimulateConsole Process() => new(Console.Out, Console.Error);
}
