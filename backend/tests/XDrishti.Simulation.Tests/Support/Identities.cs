using XDrishti.Domain.Research;
using XDrishti.Simulation.Scanning;

namespace XDrishti.Simulation.Tests.Support;

/// <summary>The identities of D2 and D10, which hold on every fixture (checked against the numbers of the tables, never against each other alone; the eligible steps are hand-counted per fixture in OrderOfDecisionTests, not summed here).</summary>
internal static class Identities
{
    public static void ShouldHold(EntryScanner scanner)
    {
        var s = scanner.Counters;
        var side = scanner.Counters.Side;

        s.DecisionSteps.ShouldBe(s.StaleBucketSteps + s.OutOfWindowSteps + s.WindowSteps, side + ": DecisionSteps = Stale + OutOfWindow + Window");
        s.Candidates.ShouldBe(scanner.Candidates.Count);
        s.FullyReadySteps.ShouldBeLessThanOrEqualTo(s.WindowSteps);
        foreach (var condition in s.Conditions)
        {
            var isInvalidate = condition.Path.Contains(".invalidate[", StringComparison.Ordinal);
            condition.Total.ShouldBe(isInvalidate ? s.DecisionSteps - s.StaleBucketSteps : s.WindowSteps, side + " " + condition.Path);
        }
    }

    /// <summary>The counts of one condition as (True, False, Unknown, NotReady).</summary>
    public static (long True, long False, long Unknown, long NotReady) Of(ScanCounters counters, string path)
    {
        var c = counters.Conditions.Single(x => x.Path == path);
        return (c.True, c.False, c.Unknown, c.NotReady);
    }
}
