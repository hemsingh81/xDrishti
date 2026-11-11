using System.Collections;
using XDrishti.Domain.Market.Bars;

namespace XDrishti.Application.Replay;

/// <summary>A read-only view of a part of a bar window (SR-01 part B): it exposes only the bars from its start to its end, never the array behind it, so a reader of a cut cannot reach a bar after the close of the cut.</summary>
internal sealed class BarSlice(Bar[] window, int start, int count) : IReadOnlyList<Bar>
{
    public int Count { get; } = count;

    public Bar this[int index] => (uint)index < (uint)Count ? window[start + index] : throw new ArgumentOutOfRangeException(nameof(index));

    public IEnumerator<Bar> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
        {
            yield return window[start + i];
        }
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}
