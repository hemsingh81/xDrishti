namespace XDrishti.Strategies.Model;

/// <summary>
/// A list of conditions, each a text of the rules language (P3-02), combined by <see cref="Mode"/>. The items are compiled one by one and evaluated one by one at list level; they are never joined into one text. Equal by
/// value: the items are compared element by element.
/// </summary>
public sealed record ConditionList(ListMode Mode, IReadOnlyList<string> Items)
{
    /// <inheritdoc />
    public bool Equals(ConditionList? other) => other is not null && Mode == other.Mode && ListEquality.Equal(Items, other.Items);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Mode, ListEquality.Hash(Items));
}
