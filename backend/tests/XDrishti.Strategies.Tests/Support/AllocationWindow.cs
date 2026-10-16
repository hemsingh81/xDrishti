using System.Runtime;

namespace XDrishti.Strategies.Tests.Support;

/// <summary>
/// One measurement of the bytes the calling thread allocates between <see cref="Begin"/> and <see cref="End"/>, read from <see cref="GC.GetAllocatedBytesForCurrentThread"/> (exact, per thread). The window is
/// protected from the garbage collector, because a collection that another thread triggers — the test runner reporting an earlier result, a background collection that began before the window — moves this counter by
/// up to the unused part of the thread's allocation quantum (a few KB) although the thread allocates nothing: <see cref="Begin"/> starts a no-GC region (best effort), and <see cref="End"/> says whether a
/// collection ran anyway (<see cref="Disturbed"/>), in which case <see cref="AllocationProbe"/> runs the scenario again.
/// <para>
/// Nothing in <see cref="Begin"/> after it reads the counter, and nothing in <see cref="End"/> before it, allocates; the code between the two calls is the code that is measured.
/// </para>
/// </summary>
internal sealed class AllocationWindow
{
    /// <summary>How much other threads may allocate during a window without ending the no-GC region (a window lasts a few milliseconds).</summary>
    private const long NoGcBudgetBytes = 4L * 1024 * 1024;

    private bool _open;

    private bool _regionStarted;

    private int _collections;

    private long _before;

    /// <summary>True once <see cref="End"/> has been called.</summary>
    public bool Completed { get; private set; }

    /// <summary>The bytes the thread allocated between <see cref="Begin"/> and <see cref="End"/>.</summary>
    public long Allocated { get; private set; }

    /// <summary>True when a garbage collection ran inside the window (or the no-GC region ended): the bytes may then include the counter's own jump and say nothing about the code.</summary>
    public bool Disturbed { get; private set; }

    public void Begin()
    {
        if (_open)
        {
            throw new InvalidOperationException("The allocation window is open already.");
        }

        _open = true;
        Completed = false;
        _regionStarted = TryStartRegion();
        _collections = GC.CollectionCount(0);
        _before = GC.GetAllocatedBytesForCurrentThread();
    }

    public void End()
    {
        var after = GC.GetAllocatedBytesForCurrentThread();
        var collections = GC.CollectionCount(0);
        if (!_open)
        {
            throw new InvalidOperationException("The allocation window was not begun.");
        }

        var regionStarted = _regionStarted;
        var regionIntact = EndRegion();
        _open = false;
        Allocated = after - _before;
        Disturbed = collections != _collections || (regionStarted && !regionIntact);
        Completed = true;
    }

    /// <summary>Leaves the no-GC region if a scenario ended inside the window (an exception), so that the next window can start one.</summary>
    internal void Abandon()
    {
        if (_open)
        {
            _open = false;
            EndRegion();
        }
    }

    private static bool TryStartRegion()
    {
        try
        {
            return GC.TryStartNoGCRegion(NoGcBudgetBytes);
        }
        catch (InvalidOperationException)
        {
            return false;   // a region is in progress already, or the latency mode does not allow one: the collection count still tells
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;   // the budget is more than this GC allows
        }
    }

    /// <summary>Ends the region if one is still running; false when there was none to end (a collection ended it, or it never started).</summary>
    private bool EndRegion()
    {
        if (!_regionStarted)
        {
            return true;
        }

        _regionStarted = false;
        if (GCSettings.LatencyMode != GCLatencyMode.NoGCRegion)
        {
            return false;
        }

        try
        {
            GC.EndNoGCRegion();
            return true;
        }
        catch (InvalidOperationException)
        {
            return false;   // a collection ended the region between the check and the call
        }
    }
}
