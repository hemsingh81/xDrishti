using System.Collections;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Simulation.Replay;

/// <summary>
/// A window onto the one list of fed bars of an instrument: the bars of one segment (the sessions between two flagged days), without a copy. <see cref="Running.Simulator.Run"/> steps the scanner of a segment from element 0 of the list it is
/// given and walks the candidates in that same list, so the decision index of a candidate is its index in this window and the window is the list the walker reads (spec P3-04, Scope 3).
/// </summary>
internal sealed class BarSegment(IReadOnlyList<Bar> source, int start, int count) : IReadOnlyList<Bar>
{
    public int Count { get; } = count;

    public Bar this[int index]
    {
        get
        {
            if ((uint)index >= (uint)Count)
            {
                throw new ArgumentOutOfRangeException(nameof(index));
            }

            return source[start + index];
        }
    }

    public IEnumerator<Bar> GetEnumerator() => Enumerable.Range(0, Count).Select(i => source[start + i]).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
