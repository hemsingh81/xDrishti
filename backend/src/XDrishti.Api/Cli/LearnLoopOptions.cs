using XDrishti.Api.Simulate;

namespace XDrishti.Api.Cli;

/// <summary>The command line of <c>xd learn loop</c> and <c>xd learn curve</c> (spec CF-12, part 12c-1): neither takes an option.</summary>
internal static class LearnLoopOptions
{
    public const string Usage = """
        usage:
          xd learn loop
            is the learning loop helping? the three arms on the same candidates (retrained against frozen, retrained against no model), the learning curve, the reliability slope and the decision-quality lift;
            the same answer as GET /api/confidence/loop
          xd learn curve
            the learning-curve part of that report: the out-of-time gain by training rows, its slope and the rows a rising curve would need
            read only: nothing is stored, retrained or changed, and no order exists; each section whose input has no source yet says unavailable and why (today none has one), and unavailable is never a pass
        exit codes: 0 done, 1 refused, 2 wrong command line, 3 failure, 130 cancelled

        """;

    /// <summary>The errors of a command line; empty when there is none to give (the verbs take no argument).</summary>
    public static IReadOnlyList<string> Check(IReadOnlyList<string> args)
    {
        ArgumentNullException.ThrowIfNull(args);
        return [.. args.Select(a => $"unknown argument '{SimulateOptions.Show(a)}'")];
    }
}
