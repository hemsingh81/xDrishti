namespace XDrishti.Indicators.Tests.Support;

/// <summary>
/// Exact "allocates nothing" measurements that a garbage collection on another thread cannot turn red. A scenario builds what it measures, warms it up, brackets the steady-state code with
/// <see cref="AllocationWindow.Begin"/> and <see cref="AllocationWindow.End"/> and asserts what is not about bytes after <c>End</c>. If a collection ran inside the window and the window shows bytes, the
/// attempt says nothing about the code (the thread counter was seen to jump by up to one allocation quantum, a few KB, when a collection started by another thread landed in the window), so the scenario runs
/// again from scratch, up to <see cref="MaxAttempts"/> times. Two outcomes are final at once: <b>zero bytes</b> (a collection was only ever seen to add to the counter, never to hide an allocation) and <b>bytes
/// without a collection</b> (the code allocated). Nothing is averaged or tolerated: one allocation of one object in 100,000 steps is reported, as the self-test in <c>AllocationProbeTests</c> proves.
/// </summary>
internal static class AllocationProbe
{
    public const int MaxAttempts = 5;

    /// <summary>
    /// Runs the scenario (which must call <see cref="AllocationWindow.Begin"/> and <see cref="AllocationWindow.End"/> once) until an attempt is conclusive, and returns that attempt's bytes, whether a collection
    /// disturbed it, and how many attempts were run. After <see cref="MaxAttempts"/> disturbed attempts the last one is returned, with <c>Disturbed</c> true.
    /// </summary>
    public static (long Allocated, bool Disturbed, int Attempts) Measure(Action<AllocationWindow> scenario)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        for (var attempt = 1; ; attempt++)
        {
            var window = new AllocationWindow();
            try
            {
                scenario(window);
            }
            finally
            {
                window.Abandon();
            }

            if (!window.Completed)
            {
                throw new InvalidOperationException("The scenario must call Begin and End on its window.");
            }

            if (window.Allocated == 0 || !window.Disturbed || attempt == MaxAttempts)
            {
                return (window.Allocated, window.Disturbed, attempt);
            }
        }
    }

    /// <summary>Fails unless the window of the scenario allocated exactly 0 bytes; <paramref name="label"/> says which case.</summary>
    public static void ShouldAllocateNothing(Action<AllocationWindow> scenario, string? label = null)
    {
        var (allocated, disturbed, attempts) = Measure(scenario);

        allocated.ShouldBe(0, disturbed ? $"{label} (a garbage collection ran in each of {attempts} attempts, so this may not be the code's own allocation)" : label);
    }
}
