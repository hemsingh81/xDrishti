using XDrishti.Strategies.Tests.Support;

namespace XDrishti.Strategies.Tests.Evaluation;

/// <summary>Self-test of the copy of <see cref="AllocationProbe"/> used by the allocation tests of this project: it sees an allocation, however small and rare, and reports zero for code that allocates nothing.</summary>
[Collection(ExclusiveRun.Name)]
public sealed class AllocationProbeSelfTests
{
    [Fact]
    public void A_window_that_allocates_nothing_reports_zero_at_the_first_attempt()
    {
        var sum = 0L;

        var (allocated, _, attempts) = AllocationProbe.Measure(window =>
        {
            window.Begin();
            for (var i = 0; i < 100_000; i++)
            {
                sum += i;
            }

            window.End();
        });

        allocated.ShouldBe(0);
        attempts.ShouldBe(1);
        sum.ShouldBeGreaterThan(0);
    }

    [Fact]
    public void One_object_in_a_hundred_thousand_steps_is_reported_and_fails_the_assertion()
    {
        object? keep = null;
        var sum = 0L;
        void Scenario(AllocationWindow window)
        {
            window.Begin();
            for (var i = 0; i < 100_000; i++)
            {
                sum += i;
                if (i == 50_000)
                {
                    keep = new byte[24];
                }
            }

            window.End();
        }

        var (allocated, disturbed, attempts) = AllocationProbe.Measure(Scenario);

        allocated.ShouldBeGreaterThanOrEqualTo(24);
        (!disturbed || attempts == AllocationProbe.MaxAttempts).ShouldBeTrue();
        Should.Throw<ShouldAssertException>(() => AllocationProbe.ShouldAllocateNothing(Scenario, "one object"));
        GC.KeepAlive(keep);
    }
}
