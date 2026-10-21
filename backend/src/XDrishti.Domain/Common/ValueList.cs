using System.Collections;

namespace XDrishti.Domain.Common;

/// <summary>An immutable list that is equal to another when its items are equal in order, so that a record holding one keeps value equality (P4-01: the legs of a trade).</summary>
/// <typeparam name="T">The item type; compared with the default equality.</typeparam>
public sealed class ValueList<T> : IReadOnlyList<T>, IEquatable<ValueList<T>>
{
    private readonly T[] _items;

    /// <summary>Copies the items.</summary>
    /// <param name="items">The items, in order.</param>
    public ValueList(IEnumerable<T> items)
    {
        ArgumentNullException.ThrowIfNull(items);
        _items = [.. items];
    }

    /// <inheritdoc />
    public int Count => _items.Length;

    /// <inheritdoc />
    public T this[int index] => _items[index];

    /// <inheritdoc />
    public bool Equals(ValueList<T>? other) => other is not null && _items.AsSpan().SequenceEqual(other._items);

    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is ValueList<T> other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var item in _items)
        {
            hash.Add(item);
        }

        return hash.ToHashCode();
    }

    /// <inheritdoc />
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)_items).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => _items.GetEnumerator();
}
