using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The command line of <c>xd learn rollback</c> (spec CF-10, part 10d-2a): it takes no option. The usage text is shared with <c>xd learn influence</c>.</summary>
internal static class LearnRollbackOptions
{
    /// <summary>The errors of a command line; empty when there is none to give.</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return [.. args.Select(a => $"unknown argument '{SimulateOptions.Show(a)}'")];
    }
}
