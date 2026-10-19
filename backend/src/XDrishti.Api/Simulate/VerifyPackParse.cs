namespace XDrishti.Api.Simulate;

/// <summary>The outcome of parsing the command line of <c>verify-pack</c>: the options, or the usage errors (never both, never neither).</summary>
internal sealed record VerifyPackParse(VerifyPackOptions? Options, IReadOnlyList<string> Errors);
