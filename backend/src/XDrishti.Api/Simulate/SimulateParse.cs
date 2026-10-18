namespace XDrishti.Api.Simulate;

/// <summary>The outcome of parsing the command line: the options, or the usage errors (never both, never neither).</summary>
internal sealed record SimulateParse(SimulateOptions? Options, IReadOnlyList<string> Errors)
{
    public bool IsValid => Options is not null;
}
